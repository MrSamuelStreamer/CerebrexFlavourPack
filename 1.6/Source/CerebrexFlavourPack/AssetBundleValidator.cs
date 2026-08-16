using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Startup validator that runs once at load and warns if any expected
/// AssetBundle file, or any expected shader/texture inside the bundles, is
/// missing.
///
/// The check runs from a static constructor so it fires during RimWorld's
/// StaticConstructorOnStartup phase, before any of the code paths that
/// actually use these assets (GameCondition_BlackHole render init, etc.).
/// Users then see a single actionable warning line at the top of their log
/// instead of discovering the problem at world-view time when the black
/// hole silently fails to render.
///
/// Extend <see cref="ExpectedBundleAssets"/> when a new bundle-sourced
/// shader or texture is added anywhere in the mod.
/// </summary>
[StaticConstructorOnStartup]
public static class AssetBundleValidator
{
    /// <summary>
    /// Platform-suffix pattern the RimWorld AssetBundle loader probes for.
    /// See the "AssetBundle suffixes" note in agent memory: only _linux,
    /// _mac, and _win are recognised. _windows is silently dropped.
    /// </summary>
    private static readonly string[] ExpectedBundleSuffixes = { "_linux", "_mac", "_win" };

    /// <summary>
    /// Bundle-name prefix produced by AssetLabeler when this mod builds
    /// its Unity project. Kept lowercase because that is how Unity writes
    /// them to disk.
    /// </summary>
    private const string BundleNamePrefix = "cerebrexflavourpack";

    /// <summary>
    /// Location of the bundle directory relative to the mod root.
    /// </summary>
    private const string BundleSubdir = "Common/AssetBundles";

    /// <summary>
    /// Rough minimum size (bytes) an intact bundle should exceed. Guards
    /// against LFS pointer stubs (~130 bytes) or truncated downloads.
    /// </summary>
    private const long MinPlausibleBundleBytes = 4096;

    /// <summary>
    /// Registry of assets the mod pulls out of its AssetBundles at runtime.
    /// Every entry here is probed on startup and logged if missing.
    /// </summary>
    private static readonly ExpectedAsset[] ExpectedBundleAssets =
    {
        new ExpectedAsset(
            assetKind: AssetKind.Shader,
            name: "BlackHoleRayMarch",
            usedBy: "GameCondition_BlackHole.RayMarchShader"),
        new ExpectedAsset(
            assetKind: AssetKind.Texture,
            name: "AccretionDisc",
            usedBy: "GameCondition_BlackHole.DiscTexture"),
    };

    static AssetBundleValidator()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            // Validator failures must never break the mod load — the whole
            // point of this class is diagnostics.
            Log.Warning($"[CerebrexFlavourPack] AssetBundle validator threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Run()
    {
        ModContentPack cfp = FindOwnModContentPack();
        if (cfp == null)
        {
            // Ran from a stripped test rig or the mod list somehow lost
            // itself. Nothing to check.
            return;
        }

        List<string> bundleWarnings = CheckBundleFiles(cfp);
        List<string> assetWarnings = CheckExpectedAssets();

        if (bundleWarnings.Count == 0 && assetWarnings.Count == 0)
        {
            Log.Message("[CerebrexFlavourPack] AssetBundle validation OK: all expected bundles and assets present.");
            return;
        }

        var msg = new System.Text.StringBuilder();
        msg.Append("[CerebrexFlavourPack] AssetBundle validation FOUND ")
           .Append(bundleWarnings.Count + assetWarnings.Count)
           .AppendLine(" problem(s):");

        foreach (string w in bundleWarnings)
        {
            msg.Append("  - ").AppendLine(w);
        }
        foreach (string w in assetWarnings)
        {
            msg.Append("  - ").AppendLine(w);
        }

        msg.AppendLine("Fix (mod author): open UnityAssets/ in Unity Editor and run")
           .AppendLine("  CerebrexFlavourPack -> Label All Assets + Build Bundles")
           .AppendLine("then reship Common/AssetBundles/cerebrexflavourpack_{linux,mac,win}.")
           .AppendLine("Fix (user): reinstall the mod from a fresh download; the bundles may not have")
           .Append("been shipped in your copy.");

        Log.Warning(msg.ToString());
    }

    private static ModContentPack FindOwnModContentPack()
    {
        // The mod is shipped under two package IDs depending on source:
        //   - MrSamuelStreamer.CerebrexFlavourPack        (source/local)
        //   - MrSamuelStreamer.CerebrexFlavourPack_Steam  (Steam workshop)
        // Match either.
        foreach (ModContentPack mcp in LoadedModManager.RunningModsListForReading)
        {
            string pid = mcp.PackageId ?? string.Empty;
            if (pid.Equals("mrsamuelstreamer.cerebrexflavourpack", System.StringComparison.OrdinalIgnoreCase) ||
                pid.Equals("mrsamuelstreamer.cerebrexflavourpack_steam", System.StringComparison.OrdinalIgnoreCase))
            {
                return mcp;
            }
        }
        return null;
    }

    private static List<string> CheckBundleFiles(ModContentPack cfp)
    {
        var warnings = new List<string>();
        string bundleDir = Path.Combine(cfp.RootDir, BundleSubdir);
        if (!Directory.Exists(bundleDir))
        {
            warnings.Add(
                $"Bundle directory missing: {bundleDir}. Expected to hold {BundleNamePrefix}_{{linux,mac,win}} bundle files.");
            return warnings;
        }

        foreach (string suffix in ExpectedBundleSuffixes)
        {
            string fileName = BundleNamePrefix + suffix;
            string filePath = Path.Combine(bundleDir, fileName);
            if (!File.Exists(filePath))
            {
                warnings.Add($"Bundle file missing: {fileName} (looked at {filePath}).");
                continue;
            }
            long len = new FileInfo(filePath).Length;
            if (len < MinPlausibleBundleBytes)
            {
                warnings.Add(
                    $"Bundle file {fileName} looks truncated (only {len} bytes). " +
                    "Fresh clone through git without LFS, or partial download.");
            }
        }
        return warnings;
    }

    private static List<string> CheckExpectedAssets()
    {
        var warnings = new List<string>();
        foreach (ExpectedAsset a in ExpectedBundleAssets)
        {
            bool found;
            switch (a.assetKind)
            {
                case AssetKind.Shader:
                    found = ContentFinder<Shader>.Get(a.name, reportFailure: false) != null;
                    break;
                case AssetKind.Texture:
                    found = ContentFinder<Texture2D>.Get(a.name, reportFailure: false) != null;
                    break;
                default:
                    // Unknown kind, skip rather than false-positive.
                    continue;
            }
            if (!found)
            {
                warnings.Add(
                    $"Expected bundle {a.assetKind} '{a.name}' not found (used by {a.usedBy}).");
            }
        }
        return warnings;
    }

    private enum AssetKind
    {
        Shader,
        Texture
    }

    private sealed class ExpectedAsset
    {
        public readonly AssetKind assetKind;
        public readonly string name;
        public readonly string usedBy;

        public ExpectedAsset(AssetKind assetKind, string name, string usedBy)
        {
            this.assetKind = assetKind;
            this.name = name;
            this.usedBy = usedBy;
        }
    }
}
