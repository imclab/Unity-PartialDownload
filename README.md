##The Problem
I keep working with mobile games built on top of Unity3D, most of which have a target executable size of 50MB. These games however have tend more than 50MB worth of assets, so they pack content up into asset bundles, and download them later using Unity3D’s WWW class. At this point these games all run into the same problem. On a flaky mobile network downloading a 2MB file can be a challenge, often the download is disrupted or worse yet the player walks into an underground tunnel (or something like that). 

##The Solution
Abandon the built in WWW class for internet downloads, simple. Use other networking functions available through the .NET runtime to download your asset bundles onto a cache folder on the disk. Once the asset is on disk, use the WWW class to load it up as an asset bundle. It sounds simple but there are a few issues we will have to deal with. First, if you want a download to be resumable you have to make sure that the file you finish downloading is the same file you started downloading. You also need to know how many bytes you are trying to download, if this information isn’t available you can’t really resume your download. Then there is the issue of coming up with a simple interface of doing a complex task. I’ll try to tackle all these issues in this article.

##Birds Eye View
We’re going to create a class to handle the loading of an asset for us. We’ll call this class RemoteFile. Ideally you should be able to create a remote file, retrieve information from it and trigger a download if needed. This is the target API:
```
RemoteFile rf = new RemoteFile("http://remoteserver.com/someasset.assetbundle", "cache/someasset.assetbundle");
yield return StartCoroutine(rf.Download));
// rf is now available, it was streamed, or skipped 
```
Simple but powerful. Our class is going to be responsible for downloading files as well as comparing to local files to see if an update is needed. Everything loads in an asynch maner, so no blocking is done. With that, lets check out what the RemoteFile class will actually look like.
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
```mRemoteFile``` and ```mLocalFile``` are string that point to the remote files url, and desired local copy. In order to determine weather a file has a newer version on the server we need to keep track of when the file was last modified, this is what ```mRemoteLastModified``` and ```mLocalLastModified``` are for. Because we will support resuming downloads, we need to store the size of the remote file in the ```mRemoteFileSize``` variable. A copy of HttpWebResponse is kept in the variable ```mAsynchResponse``` so we can monitor when the remote file has been downloaded. ```mBundle``` is a conveniance variable that points to the local copy of the asset bundle.

##Implementation Details
Lets go trough every method and accessor in this class one at a time.

####LocalBundle
```LocalBundle``` is a conveniant accessor for the ```mBundle``` variable
```
public AssetBundle LocalBundle {
	get {
		return mBundle;
	}
}
```
####IsOutdated
```IsOutdated``` is an accessor that compares the last modified time of the server file to the last modified time of the local file. If the server was modified after the local file, our local copy is outdated and needs to be redownloaded. If the server does not support returning this data (more on that in the constructor) then ```IsOutdated``` will always be true.
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
The ```Constructor``` takes two strings, the url of the remote file to load, and the path of the local file where the remote file should be saved. The ```Constructor``` is responsible for getting the _last_ _modified_ times for the remote and local file, it also gets the _size_ _in_ _bytes_ of the remote file. We get information about the remote file trough an ```HttpWebRequest``` with it's content type set to _HEAD_. Setting the content type to _HEAD_ ensures that we only get header data, not the whole file.
```
public RemoteFile(string remoteFile, string localFile) {
	mRemoteFile = remoteFile;
	mLocalFile = localFile;

	HttpWebRequest request = (HttpWebRequest)WebRequest.Create(remoteFile);
	request.Method = "HEAD"; // Only the header info, not full file!

	// Get response will throw WebException if file is not found
	HttpWebResponse resp = (HttpWebResponse)request.GetResponse();
	mRemoteLastModified = resp.LastModified;
	mRemoteFileSize = resp.ContentLength;

	resp.Close();

	if (File.Exists(mLocalFile)) {
		// This will not work in web player!
		mLocalLastModified = File.GetLastWriteTime(mLocalFile);
	}
}
```
####Download
