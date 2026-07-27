using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Places the player's single <see cref="CerebrexFlavourPackDefOf.CFP_CerebrexCore"/> on the
/// starting home map. Structure mirrors the sibling MSSFP.Questing.ScenPart_Pursuers convention.
/// </summary>
public class ScenPart_CerebrexCore : ScenPart
{
    private const float SearchRadius = 60f;

    /// <summary>Set once the core has been placed so later-generated maps do not get a second one.</summary>
    private bool placed;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref placed, "placed", false);
    }

    public override string Summary(Scenario scen)
    {
        return "CerebrexFlavourPack_ScenPart_CoreSummary".Translate();
    }

    public override void PostWorldGenerate()
    {
        base.PostWorldGenerate();
        placed = false;
    }

    public override void GenerateIntoMap(Map map)
    {
        base.GenerateIntoMap(map);
        if (placed || !map.IsPlayerHome)
        {
            return;
        }

        ThingDef def = CerebrexFlavourPackDefOf.CFP_CerebrexCore;
        if (!TryFindPlacementCell(map, def, out IntVec3 cell))
        {
            // map.Center is not guaranteed clear (water, mountain, other scenario items), but a
            // wiped forced placement still beats silently starting the run with no core at all.
            cell = map.Center;
            ModLog.Warn($"No clear {def.Size.x}x{def.Size.z} spot for {def.defName} near map centre; forcing placement at {cell}.");
        }

        Thing core = ThingMaker.MakeThing(def);
        core.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(core, cell, map, Rot4.North, WipeMode.Vanish);
        placed = true;
    }

    /// <summary>
    /// Spirals out from the map centre for a rect that is buildable, unroofed and unobstructed,
    /// rather than assuming the exact centre cell is free.
    /// </summary>
    private static bool TryFindPlacementCell(Map map, ThingDef def, out IntVec3 result)
    {
        foreach (IntVec3 candidate in GenRadial.RadialCellsAround(map.Center, SearchRadius, useCenter: true))
        {
            if (!candidate.InBounds(map))
            {
                continue;
            }

            CellRect rect = GenAdj.OccupiedRect(candidate, Rot4.North, def.Size);
            if (!rect.InBounds(map) || AnyCellRoofedOrFogged(map, rect))
            {
                continue;
            }

            if (!GenConstruct.CanPlaceBlueprintAt(def, candidate, Rot4.North, map).Accepted)
            {
                continue;
            }

            result = candidate;
            return true;
        }

        result = IntVec3.Invalid;
        return false;
    }

    private static bool AnyCellRoofedOrFogged(Map map, CellRect rect)
    {
        foreach (IntVec3 cell in rect)
        {
            if (cell.Roofed(map) || cell.Fogged(map))
            {
                return true;
            }
        }

        return false;
    }
}
