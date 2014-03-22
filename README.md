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
