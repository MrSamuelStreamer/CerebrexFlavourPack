using System;
using UnityEditor;
using UnityEngine;

public class ModAssetBundleBuilder
{
    private const string BundleName = "cerebrexflavourpack";
    private const string OutputDirectoryRoot = "../Common/AssetBundles";

    [MenuItem("Assets/Build CerebrexFlavourPack AssetBundles (LZ4)")]
    public static void BuildBundles()
    {
        // Allow overriding the bundle name from the command line (for CI)
        string assetBundleName = BundleName;
        foreach (string arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--assetBundleName="))
            {
                assetBundleName = arg.Substring("--assetBundleName=".Length);
                Debug.Log($"Using asset bundle name: {assetBundleName}");
            }
        }

        // Auto-label all assets in Assets/Data
        string[] assetPaths = AssetLabeler.LabelAllAssetsWithCommonName(assetBundleName).ToArray();
        if (assetPaths.Length == 0)
        {
            Debug.LogError("No assets were labeled; aborting asset bundle build.");
            return;
        }

        Debug.Log("Building CerebrexFlavourPack asset bundles...");
        foreach (string assetPath in assetPaths)
        {
            Debug.Log($"  Including: {assetPath}");
        }

        if (!System.IO.Directory.Exists(OutputDirectoryRoot))
            System.IO.Directory.CreateDirectory(OutputDirectoryRoot);

        // Build per-platform bundles with LZ4 compression
        AssetBundleBuild[] bundles = new AssetBundleBuild[1];

        bundles[0] = new AssetBundleBuild
        {
            assetBundleName = assetBundleName + "_linux",
            assetNames = assetPaths
        };
        BuildPipeline.BuildAssetBundles(OutputDirectoryRoot, bundles,
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneLinux64);

        bundles[0] = new AssetBundleBuild
        {
            assetBundleName = assetBundleName + "_mac",
            assetNames = assetPaths
        };
        BuildPipeline.BuildAssetBundles(OutputDirectoryRoot, bundles,
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneOSX);

        bundles[0] = new AssetBundleBuild
        {
            assetBundleName = assetBundleName + "_win",
            assetNames = assetPaths
        };
        BuildPipeline.BuildAssetBundles(OutputDirectoryRoot, bundles,
            BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);

        // Unity emits a bundle named after the output directory ("AssetBundles")
        // alongside the per-platform bundles. RimWorld does not need it and it
        // collides with same-named master bundles from other mods, which prevents
        // the platform bundles from loading. Delete it so only the platform bundles
        // ship. Also drop the sibling .manifest to keep the directory tidy.
        string masterBundle = System.IO.Path.Combine(OutputDirectoryRoot, "AssetBundles");
        string masterManifest = masterBundle + ".manifest";
        foreach (string stray in new[] { masterBundle, masterManifest })
        {
            if (System.IO.File.Exists(stray))
            {
                System.IO.File.Delete(stray);
                Debug.Log($"Deleted stray master bundle artifact: {stray}");
            }
        }

        Debug.Log("CerebrexFlavourPack asset bundles built successfully to " + OutputDirectoryRoot);
    }
}
