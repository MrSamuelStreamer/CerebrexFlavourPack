using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Restores DeepDrill placeability on lattice-covered / lattice-flipped maps.
///
/// Vanilla <see cref="DeepDrillUtility.GetBaseResource"/> returns null when either
/// (a) <c>map.Biome.hasBedrock == false</c> - which the lattice biome flip target
/// <c>AB_MechanoidIntrusion</c> explicitly is, or
/// (b) the tile has no natural rock recorded.
/// A null result makes <see cref="PlaceWorker_DeepDrill"/> refuse placement everywhere
/// no lump has already been scanned, effectively locking mining out once the biome flips.
///
/// This postfix leaves vanilla decisions untouched everywhere else and only injects a
/// fallback rock when the cell being tested is on the cerebrex lattice terrain (either
/// as the top layer or as the base beneath a floor). The stone chunk fallback the drill
/// yields when no scanned resource is present is what makes the placeworker satisfied -
/// vanilla behaviour of using scanned resources first is unchanged.
/// </summary>
[HarmonyPatch(typeof(DeepDrillUtility), nameof(DeepDrillUtility.GetBaseResource))]
public static class Harmony_DeepDrillOnLattice
{
    public static void Postfix(Map map, IntVec3 cell, ref ThingDef __result)
    {
        if (__result != null)
        {
            return;
        }

        if (!CerebrexLatticeDefs.Active)
        {
            return;
        }

        if (map == null || !cell.InBounds(map))
        {
            return;
        }

        TerrainDef top = map.terrainGrid.TerrainAt(cell);
        TerrainDef under = map.terrainGrid.BaseTerrainAt(cell);
        if (!CerebrexLatticeDefs.IsLatticeTerrain(top) && !CerebrexLatticeDefs.IsLatticeTerrain(under))
        {
            return;
        }

        // Deterministic per-cell pick so repeated placeworker calls agree with the
        // eventual drill output. Mirrors GetBaseResource's own seeded random path.
        Rand.PushState();
        Rand.Seed = cell.GetHashCode();
        ThingDef rock = Find.World.NaturalRockTypesIn(map.Tile)
            .Select(r => r.building?.mineableThing)
            .Where(t => t != null)
            .RandomElementWithFallback();
        Rand.PopState();

        __result = rock;
    }
}
