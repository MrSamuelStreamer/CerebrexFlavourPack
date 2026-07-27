using UnityEditor;
using UnityEngine;

/// <summary>
/// Top-level CerebrexFlavourPack menu in the Unity menu bar.
/// Provides a single-click entry point for the common build workflow:
///   Label All Assets → Build AssetBundles (all platforms)
///
/// Note: "Label + Build" is equivalent to just "Build" on its own because
/// BuildAssetBundles already calls AssetLabeler internally. The separate
/// label-only and build-only items are kept for when you need them individually.
/// </summary>
public static class CerebrexFlavourPackMenu
{
    private const string MenuRoot = "CerebrexFlavourPack/";

    // -----------------------------------------------------------------------
    // Primary workflow — the one-click option
    // -----------------------------------------------------------------------

    [MenuItem(MenuRoot + "Label All Assets + Build Bundles")]
    public static void LabelAndBuild()
    {
        Debug.Log("=== CerebrexFlavourPack: Label All Assets + Build Bundles ===");
        ModAssetBundleBuilder.BuildBundles();
        Debug.Log("=== CerebrexFlavourPack: Done ===");
    }

    // -----------------------------------------------------------------------
    // Individual steps (useful when iterating on asset settings or shaders)
    // -----------------------------------------------------------------------

    [MenuItem(MenuRoot + "Label All Assets")]
    public static void LabelOnly()
    {
        Debug.Log("=== CerebrexFlavourPack: Label All Assets ===");
        AssetLabeler.LabelAllAssetsWithCommonName("cerebrexflavourpack");
        Debug.Log("=== CerebrexFlavourPack: Done ===");
    }

    [MenuItem(MenuRoot + "Build Bundles (LZ4, all platforms)")]
    public static void BuildOnly()
    {
        Debug.Log("=== CerebrexFlavourPack: Build Bundles ===");
        ModAssetBundleBuilder.BuildBundles();
        Debug.Log("=== CerebrexFlavourPack: Done ===");
    }
}
