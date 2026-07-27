using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Soft dependency on Alpha Biomes. Both defs live in that mod, not this one, so they are
/// resolved by name instead of through the usual [DefOf] pattern (which requires the def to
/// always exist). If Alpha Biomes isn't loaded, both fields are null and <see cref="Active"/>
/// is false - the entire lattice feature (spread, biome flip, mood thought) goes inert.
/// </summary>
[StaticConstructorOnStartup]
public static class CerebrexLatticeDefs
{
    /// <summary>Alpha Biomes' "polymer flooring" - the dominant terrain of AB_MechanoidIntrusion.</summary>
    public static readonly TerrainDef TargetTerrain = DefDatabase<TerrainDef>.GetNamedSilentFail("GU_MetalFloor1");

    /// <summary>Alpha Biomes' "mechanoid intrusion" biome.</summary>
    public static readonly BiomeDef TargetBiome = DefDatabase<BiomeDef>.GetNamedSilentFail("AB_MechanoidIntrusion");

    public static bool Active => TargetTerrain != null && TargetBiome != null;

    static CerebrexLatticeDefs()
    {
        if (!Active)
        {
            ModLog.Log("Alpha Biomes not detected (GU_MetalFloor1 / AB_MechanoidIntrusion missing) - " +
                       "cerebrex lattice spread, biome conversion and mechanoid affinity mood are disabled.");
        }
    }
}
