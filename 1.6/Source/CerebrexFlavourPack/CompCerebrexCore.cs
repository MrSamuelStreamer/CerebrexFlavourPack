using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

public class CompProperties_CerebrexCore : CompProperties
{
    /// <summary>Fraction of MaxHitPoints restored by a single Refurbish click.</summary>
    public float refurbishBatchFraction = 0.1f;

    /// <summary>Base plasteel spent per 1% of MaxHitPoints restored, before the settings multiplier.</summary>
    public float plasteelPerPercent = 5f;

    /// <summary>Base advanced components spent per 1% of MaxHitPoints restored, before the settings multiplier.</summary>
    public float componentsPerPercent = 1f;

    public CompProperties_CerebrexCore()
    {
        compClass = typeof(CompCerebrexCore);
    }
}

public class CompCerebrexCore : ThingComp
{
    public CompProperties_CerebrexCore Props => (CompProperties_CerebrexCore)props;

    // CompGetGizmosExtra is an OnGUI-driven iterator (InspectGizmoGrid redraws it every frame
    // the thing is selected), so the affordability check it needs must not re-walk every
    // storage slot group ~60x/sec. Cached per game tick - cheap while paused (tick frozen) and
    // still correct, since Refurbish() re-verifies against live stacks at spend time regardless.
    private int cachedCountTick = -1;
    private int cachedPlasteelCount;
    private int cachedComponentsCount;

    // Vanilla CerebrexCore's floating brain (RimWorld.CompCerebrexCore.DrawAt), reproduced
    // here since this comp intentionally doesn't derive from vanilla's quest-bound comp.
    private const string BrainTexPath = "Things/Building/CerebrexCore/CerebrexCore_Brain";
    private static readonly Vector3 BrainDrawSize = new(7f, 7f, 7f);
    private const float BrainZOffset = 2f;
    private const float BrainBobHeight = 0.35f;
    private const int BrainBobPeriodTicks = 300;

    // Not saved: rebuilt on demand after load. Resolving it here rather than in
    // PostSpawnSetup keeps texture lookup off the load path.
    [Unsaved(false)]
    private Graphic cachedBrainGraphic;

    private Graphic BrainGraphic => cachedBrainGraphic ??= GraphicDatabase.Get<Graphic_Multi>(
        BrainTexPath, ShaderDatabase.Cutout, BrainDrawSize, Color.white);

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);

        if (!respawningAfterLoad && CerebrexLatticeDefs.Active)
        {
            parent.Map?.GetComponent<MapComponent_CerebrexLattice>()?.Notify_CoreSpawned(parent);
        }
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        base.PostDestroy(mode, previousMap);
        Current.Game?.GetComponent<GameComponent_CerebrexWatch>()?.Notify_CoreDestroyed(parent, mode, previousMap);
    }

    public override void DrawAt(Vector3 drawLoc, bool flip = false)
    {
        float z = BrainZOffset
            + 0.5f * (1f + Mathf.Sin(Mathf.PI * 2f * GenTicks.TicksGame / BrainBobPeriodTicks))
            * BrainBobHeight;
        Matrix4x4 matrix = Matrix4x4.TRS(
            drawLoc + new Vector3(0f, 0.35f, z), Quaternion.AngleAxis(0f, Vector3.up), Vector3.one);
        GenDraw.DrawMeshNowOrLater(
            BrainGraphic.MeshAt(Rot4.South), matrix, BrainGraphic.MatSouth, drawNow: false);
    }

    public override string CompInspectStringExtra()
    {
        if (!CerebrexLatticeDefs.Active || parent.Map == null)
        {
            return null;
        }

        MapComponent_CerebrexLattice lattice = parent.Map.GetComponent<MapComponent_CerebrexLattice>();
        if (lattice == null)
        {
            return null;
        }

        return "CerebrexFlavourPack_Inspect_LatticeSteelLoaded".Translate(Mathf.RoundToInt(lattice.SteelLoaded))
            + "\n" + "CerebrexFlavourPack_Inspect_LatticeCoverage".Translate(lattice.Coverage.ToStringPercent());
    }

    public override IEnumerable<Gizmo> CompGetGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetGizmosExtra())
        {
            yield return gizmo;
        }

        if (!parent.Spawned || parent.Faction != Faction.OfPlayer)
        {
            yield break;
        }

        if (CerebrexLatticeDefs.Active)
        {
            yield return FeedLatticeGizmo();
        }

        if (parent.HitPoints >= parent.MaxHitPoints)
        {
            yield break;
        }

        int healAmount = RefurbishBatchAmount();
        CalculateCost(healAmount, out int plasteel, out int components);
        Map map = parent.Map;

        // Deliberately NOT map.resourceCounter: that only recomputes on TicksGame % 204 == 0, so it
        // is stale while the game is paused - and gizmo clicks still fire when paused. Counting the
        // live stacks keeps the displayed affordability and the actual spend in agreement.
        EnsureCountsCached(map);
        bool affordable = cachedPlasteelCount >= plasteel && cachedComponentsCount >= components;

        Command_Action command = new()
        {
            defaultLabel = "CerebrexFlavourPack_Refurbish_Label".Translate(),
            defaultDesc = "CerebrexFlavourPack_Refurbish_Desc".Translate(
                healAmount, plasteel, components, parent.HitPoints, parent.MaxHitPoints),
            icon = ContentFinder<Texture2D>.Get("UI/Commands/Install", reportFailure: false),
            action = () => Refurbish(healAmount, plasteel, components)
        };

        if (!affordable)
        {
            command.Disabled = true;
            command.disabledReason = "CerebrexFlavourPack_Refurbish_NotEnough".Translate(plasteel, components);
        }

        yield return command;
    }

    /// <summary>Off by default - feeding the lattice draws a substantial, sustained steel stream
    /// (see the settings tooltip), and should never start ambushing a colony's steel unasked.</summary>
    private Command_Toggle FeedLatticeGizmo()
    {
        MapComponent_CerebrexLattice lattice = parent.Map.GetComponent<MapComponent_CerebrexLattice>();
        return new Command_Toggle
        {
            defaultLabel = "CerebrexFlavourPack_Lattice_Gizmo_Label".Translate(),
            defaultDesc = "CerebrexFlavourPack_Lattice_Gizmo_Desc".Translate(),
            icon = ContentFinder<Texture2D>.Get("UI/Commands/Install", reportFailure: false),
            isActive = () => lattice.Feeding,
            toggleAction = () => lattice.ToggleFeeding()
        };
    }

    /// <summary>HP restored by one click: a fixed batch, clamped to whatever damage is actually outstanding.</summary>
    private int RefurbishBatchAmount()
    {
        int batch = Mathf.Max(1, Mathf.RoundToInt(parent.MaxHitPoints * Props.refurbishBatchFraction));
        return Mathf.Min(batch, parent.MaxHitPoints - parent.HitPoints);
    }

    private void CalculateCost(int healAmount, out int plasteel, out int components)
    {
        float rate = CerebrexFlavourPackMod.settings?.refurbishCostRate ?? 1f;
        float percentHealed = healAmount / (float)parent.MaxHitPoints * 100f;
        plasteel = Mathf.CeilToInt(percentHealed * Props.plasteelPerPercent * rate);
        components = Mathf.CeilToInt(percentHealed * Props.componentsPerPercent * rate);
    }

    private void Refurbish(int healAmount, int plasteel, int components)
    {
        Map map = parent.Map;
        if (map == null)
        {
            return;
        }

        // Re-check against live stacks under the click, not the gizmo's cached value.
        if (SumStored(map, ThingDefOf.Plasteel) < plasteel || SumStored(map, ThingDefOf.ComponentSpacer) < components)
        {
            Messages.Message("CerebrexFlavourPack_Refurbish_NotEnough".Translate(plasteel, components), parent,
                MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        int plasteelPaid = ConsumeFromStorage(map, ThingDefOf.Plasteel, plasteel);
        int componentsPaid = ConsumeFromStorage(map, ThingDefOf.ComponentSpacer, components);

        // Belt and braces: never heal more than was actually paid for, even if a stack vanished
        // between the check and the spend.
        float paidFraction = Mathf.Min(
            plasteel > 0 ? plasteelPaid / (float)plasteel : 1f,
            components > 0 ? componentsPaid / (float)components : 1f);
        int healed = Mathf.FloorToInt(healAmount * paidFraction);
        if (healed <= 0)
        {
            Messages.Message("CerebrexFlavourPack_Refurbish_NotEnough".Translate(plasteel, components), parent,
                MessageTypeDefOf.RejectInput, historical: false);
            return;
        }

        parent.HitPoints = Mathf.Min(parent.MaxHitPoints, parent.HitPoints + healed);
        Messages.Message("CerebrexFlavourPack_Refurbish_Done".Translate(healed), parent,
            MessageTypeDefOf.PositiveEvent, historical: false);
    }

    /// <summary>
    /// Stacks of <paramref name="def"/> sitting in a haul destination (stockpile zone or storage
    /// building). Counting and spending both go through this, so the two can never disagree.
    /// Internal (not private): MapComponent_CerebrexLattice reuses this same stockpile-draw
    /// pattern for its steel feed rather than duplicating it.
    /// </summary>
    internal static List<Thing> StoredStacksOf(Map map, ThingDef def)
    {
        // Collect first: destroying things while walking a SlotGroup's HeldThings mutates it.
        List<Thing> stacks = new();
        foreach (SlotGroup slotGroup in map.haulDestinationManager.AllGroupsListForReading)
        {
            foreach (Thing heldThing in slotGroup.HeldThings)
            {
                if (heldThing.def == def)
                {
                    stacks.Add(heldThing);
                }
            }
        }

        return stacks;
    }

    private void EnsureCountsCached(Map map)
    {
        int tick = Find.TickManager.TicksGame;
        if (tick == cachedCountTick)
        {
            return;
        }

        cachedCountTick = tick;
        cachedPlasteelCount = SumStored(map, ThingDefOf.Plasteel);
        cachedComponentsCount = SumStored(map, ThingDefOf.ComponentSpacer);
    }

    /// <summary>Allocation-free total - unlike <see cref="StoredStacksOf"/>, doesn't materialise a list.</summary>
    internal static int SumStored(Map map, ThingDef def)
    {
        int total = 0;
        foreach (SlotGroup slotGroup in map.haulDestinationManager.AllGroupsListForReading)
        {
            foreach (Thing heldThing in slotGroup.HeldThings)
            {
                if (heldThing.def == def)
                {
                    total += heldThing.stackCount;
                }
            }
        }

        return total;
    }

    /// <summary>Spends up to <paramref name="count"/> and reports how much was actually removed.</summary>
    internal static int ConsumeFromStorage(Map map, ThingDef def, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int remaining = count;
        foreach (Thing stack in StoredStacksOf(map, def))
        {
            if (remaining <= 0)
            {
                break;
            }

            int take = Mathf.Min(remaining, stack.stackCount);
            stack.SplitOff(take).Destroy();
            remaining -= take;
        }

        return count - remaining;
    }
}
