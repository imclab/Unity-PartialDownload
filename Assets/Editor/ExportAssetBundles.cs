using UnityEngine;
using UnityEditor;

public class ExportAssetBundles {
	[MenuItem("Assets/Build AssetBundle")]
	static void ExportResource () {
		string path = EditorUtility.SaveFilePanel("Export Bundle","~/Desktop","","assetbundle");
		Object[] selection =  Selection.GetFiltered(typeof(Object), SelectionMode.DeepAssets);
		BuildPipeline.BuildAssetBundle(Selection.activeObject, selection, path, BuildAssetBundleOptions.CollectDependencies | BuildAssetBundleOptions.CompleteAssets);
	}
}