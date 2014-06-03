//#define UseWebClient

using UnityEngine;
using System.Collections;
using System.Net;
using System.IO;
using System.ComponentModel;
using System;

public class RemoteFile {
	static bool gDownloadIsRunning = false;
	
	protected string mRemoteFile;
	protected string mLocalFile;
	
	protected System.DateTime mRemoteLastModified;
	protected System.DateTime mLocalLastModified;
	
	protected AssetBundle mBundle = null;
	protected long mRemoteFileSize =  0;
	protected bool mHadError = false;
	#if UseWebClient || UNITY_IOS
	bool done = false;
	#else
	protected HttpWebResponse mAsynchResponse = null;
	#endif
	
	protected void Log(string message) {
		Debug.Log("[ResumableDownload] " + message);
	}
	
	public RemoteFile(string remoteFile, string localFile) {
		Log("Creating download for: " + remoteFile);
		mRemoteFile = remoteFile;
		mLocalFile = localFile;
		
		if (File.Exists(mLocalFile)) { // This will not work in web player!
			mLocalLastModified = File.GetLastWriteTime(mLocalFile);
		}
		
		HttpWebRequest request = (HttpWebRequest)System.Net.WebRequest.Create(remoteFile);
		request.Method = "HEAD"; // Only the header info, not full file!
		
		// Get response will throw WebException if file is not found
		HttpWebResponse resp = null;
		try {
			resp = (HttpWebResponse)request.GetResponse();
		}
		catch (System.Exception e) {
			Debug.LogWarning("<color=red>ERROR: " + remoteFile + "</color>");
			Debug.LogWarning("ERROR: " + e);
			//Log("Constructor Exception: " + e);
			mHadError = true;
			return;
		}
		
		mRemoteLastModified = resp.LastModified;
		mRemoteFileSize = resp.ContentLength;
		
		resp.Close();
	}
	
	public AssetBundle LocalBundle {
		get {
			return mBundle;
		}
	}
	
	public bool IsOutdated { 
		get {
			if (File.Exists(mLocalFile))
				return mRemoteLastModified > mLocalLastModified;
			return true;
		}
	}
	
	// It's the callers responsibility to start this as a coroutine!
	public IEnumerator Download() { 
		if (mHadError) { yield break; }
		
		while (gDownloadIsRunning)
			yield return null;
		gDownloadIsRunning = true;
		
		long localFileSize = (File.Exists(mLocalFile))? (new FileInfo(mLocalFile)).Length : 0;
		if (localFileSize == mRemoteFileSize && !IsOutdated) {
			Log("File already cacled, not downloading: " + mLocalFile);
			gDownloadIsRunning = false;
			yield break; // We already have the file, early out
		}
		else if (localFileSize > mRemoteFileSize || IsOutdated) {
			if (!IsOutdated) Debug.LogWarning("Local file is larger than remote file, but not outdated. PANIC!");
			if (IsOutdated) Debug.Log("Local file is outdated, deleting");
			try {
				if (File.Exists(mLocalFile))
					File.Delete(mLocalFile);
			}
			catch (System.Exception e) {
				Debug.LogWarning("<color=red>Could not delete local file</color>");
				Debug.LogError(e);
			}
			while (File.Exists(mLocalFile))
				yield return null;
			localFileSize = 0;
		}
		
		#if UseWebClient  || UNITY_IOS
		using (WebClient client = new WebClient()) {
			client.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadCompleted);
			client.DownloadFileAsync(new Uri(mRemoteFile), mLocalFile);
		}
		
		while (!done)
			yield return null;
		#else
		int bufferSize = 1024 * 1000;
		Log("[Downloading: " + mRemoteFile);
		HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create(mRemoteFile);
		request.Timeout = 30000; 
		request.AddRange((int)localFileSize, (int)mRemoteFileSize - 1);
		request.Method = WebRequestMethods.Http.Post;
		request.BeginGetResponse(AsynchCallback, request);
		
		while (mAsynchResponse == null) // Wait for asynch to finish
			yield return null;
		
		Stream inStream = mAsynchResponse.GetResponseStream();
		
		FileStream outStream = new FileStream(mLocalFile, (localFileSize > 0)? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
		
		int count = 0;
		byte[] buff = new byte[bufferSize]; 
		while ((count = inStream.Read(buff, 0, bufferSize)) > 0) {
			outStream.Write(buff, 0, count);
			outStream.Flush();
			yield return null;
		}
		
		outStream.Flush();
		outStream.Close();
		inStream.Close();
		request.Abort();
		mAsynchResponse.Close();
		mAsynchResponse = null;
		
		localFileSize = (File.Exists(mLocalFile))? (new FileInfo(mLocalFile)).Length : 0;
		while(localFileSize != mRemoteFileSize) {
			localFileSize = (File.Exists(mLocalFile))? (new FileInfo(mLocalFile)).Length : 0;
			yield return null;
		}
		#endif
		gDownloadIsRunning = false;
	}
	
	#if UseWebClient  || UNITY_IOS
	protected void DownloadCompleted(System.Object sender, AsyncCompletedEventArgs e) {
		done = true;
	}
	#else
	// Throwind an exception here will not propogate to unity!
	protected void AsynchCallback(IAsyncResult result) {
		if (result == null) Debug.LogError("Asynch result is null!");
		
		HttpWebRequest webRequest = (HttpWebRequest)result.AsyncState;
		if (webRequest == null) Debug.LogError("Could not cast to web request");
		
		mAsynchResponse = webRequest.EndGetResponse(result) as HttpWebResponse;
		if (mAsynchResponse == null) Debug.LogError("Asynch response is null!");
		
		Log("Download compleate");
	}
	#endif

	// Don't forget to start this as a coroutine!
	public IEnumerator LoadLocalBundle() { 
		WWW loader = new WWW("file://" + mLocalFile);

		yield return loader;
		if (loader.error != null)
			throw new Exception("Loading error:" + loader.error);

		mBundle = loader.assetBundle;
	}

	public void UnloadLocalBundle() {
		if (mBundle != null)
			mBundle.Unload (false);
		mBundle = null;
	}
}
