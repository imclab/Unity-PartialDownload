using System;
using UnityEngine;
using System.Collections;

public class test : MonoBehaviour {
	string asset1Url = "http://gaborszauer.com/Axe.assetbundle";
	string asset2Url = "http://gaborszauer.com/Cart.assetbundle";
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
