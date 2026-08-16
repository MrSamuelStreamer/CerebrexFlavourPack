using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

public class CompCerebrexScanner : ThingComp
{
    public CompProperties_CerebrexScannerMinerals Props
    {
        get => props as CompProperties_CerebrexScannerMinerals;
    }

    public MapComponent_CerebrexLattice Lattice => parent.Map.GetComponent<MapComponent_CerebrexLattice>();
    public int ScanIntervalBaseTicks => Mathf.CeilToInt(Mathf.Max(0.01f, CerebrexFlavourPackMod.settings.scannerScanIntervalDays) * GenDate.TicksPerDay);

    // Lattice may not exist on this map (defs absent, or not yet spawned) - treat that as 0 coverage
    // rather than crashing the tick.
    public int ScanIntervalInTicks => Mathf.FloorToInt(ScanIntervalBaseTicks * Mathf.Max(0.1f, 1f - (Lattice?.Coverage ?? 0f)));

    public int NextScanAtTick = -1;

    public override void PostExposeData()
    {
        Scribe_Values.Look(ref NextScanAtTick, "NextScanAtTick", -1);
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        if (respawningAfterLoad || parent.Map.Biome.hasBedrock)
            return;
        Messages.Message("MessageGroundPenetratingScannerNoBedrock".Translate(parent.Named("THING")), (Thing) parent, MessageTypeDefOf.NegativeEvent, false);
    }

    // CFP_CerebrexCore uses tickerType Rare, which Thing.DoTick dispatches to CompTickRare -
    // NOT CompTickInterval (that's Normal-only). Hook both so this comp works regardless of
    // which ticker type the def ends up using; whichever one the engine actually calls fires
    // the same check exactly once per real tick, so there's no double-scan risk.
    public override void CompTickRare() => TickScan();

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);
        TickScan();
    }

    private void TickScan()
    {
        if (NextScanAtTick > Find.TickManager.TicksGame)
            return;

        NextScanAtTick = Find.TickManager.TicksGame + ScanIntervalInTicks;
        DoFind();
    }

    public int TicksUntilScan => NextScanAtTick - Find.TickManager.TicksGame;

    public override string CompInspectStringExtra()
    {
        return "CFP_CerebrexScanner".Translate(Mathf.Max(0, TicksUntilScan).ToStringTicksToPeriod());
    }

    public override void PostDrawExtraSelectionOverlays()
    {
        parent.Map.deepResourceGrid.MarkForDraw();
    }

    protected void DoFind()
    {
        Map map = parent.Map;
        IntVec3 result;
        if (!CellFinderLoose.TryFindRandomNotEdgeCellWith(10, x => CanScatterAt(x, map), map, out result))
        {
            Log.Error("Could not find a center cell for cerebrex scanning lump generation!");
            return;
        }

        ThingDef def = ChooseLumpThingDef();
        int numCells = Mathf.CeilToInt(def.deepLumpSizeRange.RandomInRange);
        foreach (IntVec3 intVec3 in GridShapeMaker.IrregularLump(result, map, numCells))
        {
            if (CanScatterAt(intVec3, map) && !intVec3.InNoBuildEdgeArea(map))
                map.deepResourceGrid.SetAt(intVec3, def, def.deepCountPerCell);
        }

        Find.LetterStack.ReceiveLetter("LetterLabelDeepScannerFoundLump".Translate() + ": " + def.LabelCap,
            "CFP_LetterDeepScannerFoundLump".Translate((NamedArgument) def.label), LetterDefOf.PositiveEvent, new LookTargets(result, map));
    }

    private bool CanScatterAt(IntVec3 pos, Map map)
    {
        int index = CellIndicesUtility.CellToIndex(pos, map.Size.x);
        TerrainDef terrainDef = map.terrainGrid.BaseTerrainAt(pos);
        return (terrainDef is not { IsWater: true } || terrainDef.passability != Traversability.Impassable) &&
               pos.GetAffordances(map).Contains(ThingDefOf.DeepDrill.terrainAffordanceNeeded) && !map.deepResourceGrid.GetCellBool(index);
    }

    protected ThingDef ChooseLumpThingDef()
    {
        return DefDatabase<ThingDef>.AllDefs
            .Where(def => def.deepCommonality > 0f)
            .RandomElementByWeight(def => def.deepCommonality);
    }
}
