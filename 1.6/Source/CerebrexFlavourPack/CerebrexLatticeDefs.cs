using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Soft dependency on Alpha Biomes. Both defs live in that mod, not this one, so they are
/// resolved by name instead of through the usual [DefOf] pattern (which requires the def to
/// always exist). If Alpha Biomes isn't loaded, both fields are null and <see cref="Active"/>
/// is false - the entire lattice feature (spread, biome flip, mood thought) goes inert.
///
/// The lattice mirrors AB_MechanoidIntrusion's own terrainPatchMakers so a fed map ends up
/// visually indistinguishable from a naturally-generated one: a MetalFloor1 base, sparser
/// MetalFloor2/3 patches, and the AB_SoilOnCrackedMetal / GU_Piping overlay. Each patch
/// terrain is resolved silently - a mod update dropping one of them degrades to whichever
/// still exist, without breaking the base spread.
/// </summary>
[StaticConstructorOnStartup]
public static class CerebrexLatticeDefs
{
    /// <summary>Alpha Biomes' "polymer flooring" - the dominant terrain of AB_MechanoidIntrusion.</summary>
    public static readonly TerrainDef TargetTerrain = DefDatabase<TerrainDef>.GetNamedSilentFail("GU_MetalFloor1");

    /// <summary>Secondary patch terrain (mid-density mechanoid plating).</summary>
    public static readonly TerrainDef TargetTerrain2 = DefDatabase<TerrainDef>.GetNamedSilentFail("GU_MetalFloor2");

    /// <summary>Tertiary patch terrain (highest-density mechanoid plating, sparse).</summary>
    public static readonly TerrainDef TargetTerrain3 = DefDatabase<TerrainDef>.GetNamedSilentFail("GU_MetalFloor3");

    /// <summary>Overlay: soil breaking through cracked metal (large mid-range patches).</summary>
    public static readonly TerrainDef SoilOnCrackedMetal = DefDatabase<TerrainDef>.GetNamedSilentFail("AB_SoilOnCrackedMetal");

    /// <summary>Overlay: exposed mechanoid piping (rare, on top of everything else).</summary>
    public static readonly TerrainDef Piping = DefDatabase<TerrainDef>.GetNamedSilentFail("GU_Piping");

    /// <summary>Alpha Biomes' "mechanoid intrusion" biome.</summary>
    public static readonly BiomeDef TargetBiome = DefDatabase<BiomeDef>.GetNamedSilentFail("AB_MechanoidIntrusion");

    /// <summary>Every terrain considered "converted" for lattice bookkeeping - base plus every patch/overlay we resolved.</summary>
    public static readonly HashSet<TerrainDef> LatticeTerrains = new();

    public static bool Active => TargetTerrain != null && TargetBiome != null;

    public static bool IsLatticeTerrain(TerrainDef t) => t != null && LatticeTerrains.Contains(t);

    static CerebrexLatticeDefs()
    {
        if (TargetTerrain != null) LatticeTerrains.Add(TargetTerrain);
        if (TargetTerrain2 != null) LatticeTerrains.Add(TargetTerrain2);
        if (TargetTerrain3 != null) LatticeTerrains.Add(TargetTerrain3);
        if (SoilOnCrackedMetal != null) LatticeTerrains.Add(SoilOnCrackedMetal);
        if (Piping != null) LatticeTerrains.Add(Piping);

        if (!Active)
        {
            ModLog.Log("Alpha Biomes not detected (GU_MetalFloor1 / AB_MechanoidIntrusion missing) - " +
                       "cerebrex lattice spread, biome conversion and mechanoid affinity mood are disabled.");
        }
    }
}
