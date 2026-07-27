using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace CerebrexFlavourPack;

/// <summary>
/// Siege graph that drops into <see cref="LordJob_CerebrexAssault"/> instead of vanilla
/// LordJob_AssaultColony when the bombardment ends.
/// </summary>
/// <remarks>
/// VENDOR COPY, pinned to RimWorld 1.6 (Odyssey). LordJob_Siege.CreateGraph is monolithic and its
/// siegeSpot/blueprintPoints fields are private, so a subclass cannot call base and patch the one
/// subgraph it needs. Vanilla's transition triggers and timings are reproduced verbatim below - if
/// a future game update retunes vanilla siege behaviour, this copy will silently keep the old
/// numbers and needs re-syncing against RimWorld/LordJob_Siege.cs.
/// </remarks>
public class LordJob_CerebrexSiege : LordJob
{
    private Faction faction;

    private IntVec3 siegeSpot;

    private float blueprintPoints;

    private List<Thing> targets;

    private float retreatFraction = 0.5f;

    // Both timings are rolled once at construction and scribed. CreateGraph re-runs on load, so
    // re-rolling them there would hand a reloaded save a different graph than the one it saved.
    private int assaultGiveUpTicks;

    private int siegeTimeoutTicks;

    // Captured at siege start so bombardment damage counts toward the assault's retreat threshold.
    // Must be scribed and passed down: the assault subgraph is rebuilt by CreateGraph on every load,
    // and letting it re-snapshot there would reset the baseline to the already-damaged current HP.
    private float coreHpBaseline;

    public override bool GuiltyOnDowned => true;

    public LordJob_CerebrexSiege()
    {
    }

    public LordJob_CerebrexSiege(Faction faction, IntVec3 siegeSpot, float blueprintPoints, Thing target,
        float retreatFraction = 0.5f)
    {
        this.faction = faction;
        this.siegeSpot = siegeSpot;
        this.blueprintPoints = blueprintPoints;
        this.retreatFraction = retreatFraction;
        targets = new List<Thing> { target };
        assaultGiveUpTicks = LordJob_CerebrexAssault.RollGiveUpTicks();
        siegeTimeoutTicks = (int)(60000f * Rand.Range(1.5f, 3f));
        coreHpBaseline = LordJob_CerebrexAssault.TotalHitPointsOf(targets);
    }

    public override StateGraph CreateGraph()
    {
        StateGraph stateGraph = new();
        LordToil travelToil = stateGraph.AttachSubgraph(new LordJob_Travel(siegeSpot).CreateGraph()).StartingToil;

        LordToil_Siege siegeToil = new(siegeSpot, blueprintPoints);
        stateGraph.AddToil(siegeToil);

        LordToil_ExitMap exitToil = new(LocomotionUrgency.Jog, canDig: false, interruptCurrentJob: true)
        {
            useAvoidGrid = true
        };
        stateGraph.AddToil(exitToil);

        // The one deliberate divergence from vanilla LordJob_Siege: the follow-up assault is focused
        // on the cerebrex core rather than the colony at large.
        LordToil assaultToil = stateGraph.AttachSubgraph(
            new LordJob_CerebrexAssault(faction, targets, sappers: false, useAvoidGridSmart: true,
                retreatFraction, assaultGiveUpTicks, coreHpBaseline).CreateGraph()).StartingToil;

        Transition arrived = new(travelToil, siegeToil);
        arrived.AddTrigger(new Trigger_Memo("TravelArrived"));
        arrived.AddTrigger(new Trigger_TicksPassed(5000));
        stateGraph.AddTransition(arrived);

        Transition beginAssault = new(siegeToil, assaultToil);
        beginAssault.AddTrigger(new Trigger_Memo("NoBuilders"));
        beginAssault.AddTrigger(new Trigger_Memo("NoArtillery"));
        beginAssault.AddTrigger(new Trigger_PawnHarmed(0.08f));
        beginAssault.AddTrigger(new Trigger_FractionPawnsLost(0.3f));
        beginAssault.AddTrigger(new Trigger_TicksPassed(siegeTimeoutTicks));
        beginAssault.AddPreAction(new TransitionAction_Message(
            "MessageSiegersAssaulting".Translate(faction.def.pawnsPlural, faction), MessageTypeDefOf.ThreatBig));
        beginAssault.AddPostAction(new TransitionAction_WakeAll());
        stateGraph.AddTransition(beginAssault);

        Transition peaceBrokeOut = new(siegeToil, exitToil);
        peaceBrokeOut.AddSource(assaultToil);
        peaceBrokeOut.AddSource(travelToil);
        peaceBrokeOut.AddTrigger(new Trigger_BecameNonHostileToPlayer());
        peaceBrokeOut.AddPreAction(new TransitionAction_Message(
            "MessageRaidersLeaving".Translate(faction.def.pawnsPlural.CapitalizeFirst(), faction.Name)));
        stateGraph.AddTransition(peaceBrokeOut);

        return stateGraph;
    }

    public override void ExposeData()
    {
        Scribe_References.Look(ref faction, "faction");
        Scribe_Values.Look(ref siegeSpot, "siegeSpot");
        Scribe_Values.Look(ref blueprintPoints, "blueprintPoints", 0f);
        Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
        Scribe_Values.Look(ref retreatFraction, "retreatFraction", 0.5f);
        Scribe_Values.Look(ref assaultGiveUpTicks, "assaultGiveUpTicks", 0);
        Scribe_Values.Look(ref siegeTimeoutTicks, "siegeTimeoutTicks", 0);
        Scribe_Values.Look(ref coreHpBaseline, "coreHpBaseline", 0f);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            targets ??= new List<Thing>();
            targets.RemoveAll(t => t == null);
        }
    }
}
