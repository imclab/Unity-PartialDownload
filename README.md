##About
This project is intended to help with downloading large files, especially over unstable network connections. It allows for files to be downloaded only in part, then resumed later. The project is built using Unity3D v4.3. To test, drag the ```test.cs``` script onto the main camera of a scene.

##The Problem
I keep working with mobile games built using Unity, most of which have a target executable size of 50MB. These games however  tend to have more than 50MB worth of assets, so they pack content up into asset bundles, and download them later using Unity’s WWW class. At this point these games all run into the same problem. On a flaky mobile network downloading a 2MB file can be a challenge, often the download is disrupted or worse yet the player walks into an underground tunnel (or something like that).

##The Solution
Abandon the built in WWW class for internet downloads, instead use HttpWebRequest to download the asset bundle to disk. Once the asset is on disk, use the WWW class to load it up as an asset bundle. It sounds simple but there are a few issues we will have to deal with. First, if you want a download to be resumable you have to make sure that the file you finish downloading is the same file you started downloading. You also need to know how many bytes you are trying to download, if this information isn’t available you can’t really resume your download. Then there is the issue of coming up with a simple interface of doing a complex task. I’ll try to tackle all these issues in this article.

##Why HttpWebRequest?
The short answer is AddRange. In order to resume downloads we need to be able to specify the start and end bytes of the web stream. While C# offers many classes for downloading files, such as WebClient almost all of them have the Range header disabled. HttpWebRequest does not. There is also the option of using third party libraries for this functionality, but most of them are built on top of the same methods we are going to use, they offer really little added value.

##Birds Eye View
We’re going to create a class to handle the loading of an asset for us. We’ll call this class RemoteFile. Ideally you should be able to create a remote file, retrieve information from it and trigger a download if needed. This is the target API:
```
RemoteFile rf = new RemoteFile("http://remoteserver.com/someasset.assetbundle", "cache/someasset.assetbundle");
yield return StartCoroutine(rf.Download));
// rf is now available, it was streamed, or skipped 
```

Simple but powerful. Our class is going to be responsible for downloading files as well as comparing to local files to see if an update is needed. Everything load in the background, not blocking the application. The actual class will look like:
```
public class RemoteFile {
    protected string mRemoteFile;
    protected string mLocalFile;
    protected System.DateTime mRemoteLastModified;
    protected System.DateTime mLocalLastModified;
    protected long mRemoteFileSize =  0;
    protected HttpWebResponse mAsynchResponse = null;
    protected AssetBundle mBundle = null;
 
    public AssetBundle LocalBundle;
    public bool IsOutdated;
    public RemoteFile(string remoteFile, string localFile);
    public IEnumerator Download();
    protected void AsynchCallback(IAsyncResult result);
    public IEnumerator LoadLocalBundle();
    public void UnloadLocalBundle();
}
```

##What are those variables?
* mRemoteFile  
  * String that stores the url of the remote file to be downloaded  
* mLocalFile
  * String that stores the uri of the local copy, that remote file is to be downloaded to  
* mRemoteLastModified
  * Timestamp of when the remote file was last modified
  * Used to check if the file needs to be re-dowloaded
* mLocalLastModified
  * Timestamp of when the local file was last modified
  * Used to check if the file needs to be re-downloaded
* mRemoteFileSize
  * Size (in bytes) of the remote file
  * Needed for the AddRange method
* mAsynchResponse
  * HttpWebResponse response token
  * Used to signal when download finishes
  * Used to get file stream that gets written to disk
* mBundle
  * Convenient access to the local copy of the file (If it's an assetbundle)

##Implementation Details
Lets go trough every method and accessor in this class one at a time.

####LocalBundle
LocalBundle is a convenient accessor for the mBundle variable
```
public AssetBundle LocalBundle {
    get {
        return mBundle;
    }
}
```

####IsOutdated
IsOutdated is an accessor that compares the last modified time of the server file to the last modified time of the local file. If the server was modified after the local file, our local copy is outdated and needs to be re-downloaded. If the server does not support returning this data (more on that in the constructor) then IsOutdated will always be true.
```
public bool IsOutdated { 
    get {
        if (File.Exists(mLocalFile))
            return mRemoteLastModified > mLocalLastModified;
        return true;
    }
}
```

####Constructor
The Constructor takes two strings, the url of the remote file to load, and the path of the local file where the remote file should be saved. The Constructor is responsible for getting the last modified times for the remote and local file, it also gets the size in bytes of the remote file. We get information about the remote file trough an HttpWebRequest with it's content type set to HEAD. Setting the content type to HEAD ensures that we only get header data, not the whole file.
```
public RemoteFile(string remoteFile, string localFile) {
    mRemoteFile = remoteFile;
    mLocalFile = localFile;
 
    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(remoteFile);
    request.Method = "HEAD"; // Only the header info, not full file!
```
Setting the requests method to HEAD ensures we don't get the full file, only header data. Because this is such a small amount of data we retrieve it with a blocking function. Beware, not all servers support the HEAD tag
```
    // Get response will throw WebException if file is not found
    HttpWebResponse resp = (HttpWebResponse)request.GetResponse();
    mRemoteLastModified = resp.LastModified;
    mRemoteFileSize = resp.ContentLength;
```
The only return data that we will use from here is the files size, and last modified time. Much like the HEAD tag there is no guarantee that every server will support providing this information. If not available, the current time will be returned for last modified and zero will be returned for the file size.
```
    resp.Close();
 
    if (File.Exists(mLocalFile)) {
        // This will not work in web player!
        mLocalLastModified = File.GetLastWriteTime(mLocalFile);
    }
}
```
Not all hosts support the HEAD command. If your host does not, it will serve up the full file and defeat the purpose of this function. Some hosts like Dropbox will support the HEAD command but will serve up invalid last modified times. If this happens IsOurdated will always be true and your file will always be downloaded, defeating the purpose of this class.

It is your responsibility as the programmer to run this code against your production server and make sure that all data is being returned as expected (HEAD is supported, and the correct last modified time gets returned.

####Download
The Download coroutine will download the desired file using an HttpWebRequest. Unlike the Constructor we can't use the GetResponse method because GetResponse is a blocking call. In order to go full asynch the WebRequest class contains a BeginGetResponse method. BeginGetResponse takes two arguments, a callback which in our case its the AsynchCallback method and a user object, which will be the response it's self. Once the download is compleate we just write the result of the stream to disk.
```
// It's the callers responsibility to start this as a coroutine!
public IEnumerator Download() { 
    int bufferSize = 1024 * 1000;
    long localFileSize = (File.Exists(mLocalFile))? (new FileInfo(mLocalFile)).Length : 0;
 
    if (localFileSize == mRemoteFileSize && !IsOutdated) {
        Debug.Log("File already cacled, not downloading");
        yield break; // We already have the file, early out
    }
    else if (localFileSize > mRemoteFileSize || IsOutdated) {
        if (!IsOutdated) Debug.LogWarning("Local file is larger than remote file, but not outdated. PANIC!");
        if (IsOutdated) Debug.Log("Local file is outdated, deleting");
        if (File.Exists(mLocalFile))
            File.Delete(mLocalFile);
        localFileSize = 0;
    }
```
If the local file is large than the remote file we panic and delete the local file. If the remote file was modified more recently than the local file, the local copy is assumed outdated and is deleted.
```
    HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(mRemoteFile);
    request.Timeout = 30000; 
    request.AddRange((int)localFileSize, (int)mRemoteFileSize - 1);
    request.Method = WebRequestMethods.Http.Post;
    request.BeginGetResponse(AsynchCallback, request);
 
    while (mAsynchResponse == null) // Wait for asynch to finish
        yield return null;
```
A few things happen here, first a timeout must be set. If the timeout is not set, the download will just hang. We provide the request with a range of bytes to download. While the AddRange documentation claims that given a single argument (the size of the local file) it should download the entire file from that starting point. In my experience this isn't the case, if a start and end byte is not provided the download just hangs. The method of the request is set to POST, this is because the previous calls set some body data and we can no longer send a get request. We next call BeginResponse, which will trigger the AsynchCallback method. Until AsynchCallback is called mAsynchResponse will be null, so we can yield on this.
```
    Stream inStream = mAsynchResponse.GetResponseStream();
    FileStream outStream = new FileStream(mLocalFile, (localFileSize > 0)? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
 
    int count = 0;
    byte[] buff = new byte[bufferSize]; 
    while ((count = inStream.Read(buff, 0, bufferSize)) > 0) {
        outStream.Write(buff, 0, count);
        outStream.Flush();
        yield return null;
    }
 
    outStream.Close();
    inStream.Close();
    request.Abort();
    mAsynchResponse.Close();
}
```
Finally we write the downloaded file stream to disk and do some cleanup. It's important remember that this is a coroutine. If called from another class be sure to call it as such. While IsOutdated is  public, you don't need to worry about checking it. The Download function will early out and not download if the file is already up to date.

####AsynchCallback
The AsynchCallback Gets called from the BeginResponse method when it's ready. The request object was passed into the BeginResponse method, it will be forwarded to the AsynchCallback callback. From there we can get the HttpWebResponse and let the Download function finish executing.
```
// Throwing an exception here will not propogate to unity!
protected void AsynchCallback(IAsyncResult result) {
    if (result == null) Debug.LogError("Asynch result is null!");
 
    HttpWebRequest webRequest = (HttpWebRequest)result.AsyncState;
    if (webRequest == null) Debug.LogError("Could not cast to web request");
 
    mAsynchResponse = webRequest.EndGetResponse(result) as HttpWebResponse;
    if (mAsynchResponse == null) Debug.LogError("Asynch response is null!");
 
    Debug.Log("Download compleate");
}
```

###LoadLocalBundle
LoadLocalBundle is a utility function to load the asset bundle we downloaded from disk. The only thing to note here is that in order to load the file from disk with the WWW class we must add file:// to the beginning.
```
// Don't forget to start this as a coroutine!
public IEnumerator LoadLocalBundle() { 
    WWW loader = new WWW("file://" + mLocalFile);
    yield return loader;
    if (loader.error != null)
        throw new Exception("Loading error:" + loader.error);
    mBundle = loader.assetBundle;
}
```

####UnloadLocalBundle
This is the counter to LoadLocalBundle. Managing memory is important, you do not want to have an asset bundle hanging around if it is not absolutely necessary.
```
public void UnloadLocalBundle() {
    if (mBundle != null)
        mBundle.Unload (false);
    mBundle = null;
}
```

####Testing
The last thing to do is to test the code, i made a quick utility class for this
```
using System;
using UnityEngine;
using System.Collections;
 
public class test : MonoBehaviour {
    string asset1Url = "http://url.com/Axe.assetbundle";
    string asset2Url = "http://url.com/Cart.assetbundle";
    bool asset1Imported = false;
    bool asset2Imported = false;
 
    string CacheDirectory {
        get {
            #if UNITY_EDITOR
            return Application.persistentDataPath + "/";
            #elif UNITY_IPHONE          
            string fileNameBase = Application.dataPath.Substring(0, Application.dataPath.LastIndexOf('/'));
            return fileNameBase.Substring(0, fileNameBase.LastIndexOf('/')) + "/Documents/";
            #elif UNITY_ANDROID
            return Application.persistentDataPath + "/";
            #else
            return Application.dataPath + "/";
            #endif
        }
    }
 
    void OnGUI() {
        GUILayout.Label("Asset path: " + CacheDirectory);
 
        if (!asset1Imported && GUILayout.Button ("Import Axe")) {
            StartCoroutine (InstantiateAsset(asset1Url, CacheDirectory + "axe.assetbundle"));
            asset1Imported = true;
        }
        if (!asset2Imported && GUILayout.Button ("Import Cart")) {
            StartCoroutine (InstantiateAsset(asset2Url, CacheDirectory + "cart.assetbundle"));
            asset2Imported = true;
        }
    }
 
    IEnumerator InstantiateAsset(string remoteAsset, string localFile) {
        RemoteFile rf = new RemoteFile(remoteAsset, localFile);
        yield return StartCoroutine(rf.Download());
        yield return StartCoroutine(rf.LoadLocalBundle());
        Instantiate(rf.LocalBundle.mainAsset);
        rf.UnloadLocalBundle ();
    }
}
```

####What next?
It may seem like the hard part is out of the way, but really this was the easy part. It's up to you to come up with an efficient strategy for managing asset bundles. The real work is in making the system that manages bundles and the memory associated with them. To keep memory sane, download a bundle, load it into memory, instantiate any needed game objects and unload the bundle.

####Update
This class was originally designed for an Android game. Porting the game to iOS, i nothiced the Asynch request crashes doe to reflection. I've updated the source (not the article tough) with a workaround.
