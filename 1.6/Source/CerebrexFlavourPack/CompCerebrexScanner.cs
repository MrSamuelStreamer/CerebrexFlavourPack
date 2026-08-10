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

    public int ScanIntervalInTicks => Mathf.FloorToInt(ScanIntervalBaseTicks * Mathf.Max(0.1f, 1 - Lattice.Coverage));

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

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);

        if (NextScanAtTick < Find.TickManager.TicksGame)
        {
            NextScanAtTick = Find.TickManager.TicksGame + ScanIntervalInTicks;
            DoFind();
        }
    }

    public int TicksUntilScan => NextScanAtTick - Find.TickManager.TicksGame;

    public override string CompInspectStringExtra()
    {
        return "CFP_CerebrexScanner".Translate(TicksUntilScan.ToStringTicksToPeriod());
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
            Log.Error("Could not find a center cell for cerebrex scanning lump generation!");
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
        return DefDatabase<ThingDef>.AllDefs.RandomElementByWeight(def => def.deepCommonality);
    }
}
