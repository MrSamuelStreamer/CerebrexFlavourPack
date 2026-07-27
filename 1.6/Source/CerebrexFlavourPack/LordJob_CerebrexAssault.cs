using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace CerebrexFlavourPack;

/// <summary>
/// Fires once the raid has chewed <c>damageFraction</c> of the target's max HP off it
/// <em>during this lord job</em>.
/// </summary>
/// <remarks>
/// Vanilla Trigger_ThingsDamageTaken compares absolute HitPoints/MaxHitPoints against a fixed
/// threshold, which is wrong here: a raid arriving at a core already sitting below the threshold
/// would retreat on tick one without a fight, and a core the player could no longer refurbish back
/// above it would become permanently immune to every future raid. Measuring damage against a
/// baseline captured when the job was created keeps the design's attrition-across-waves intent.
/// </remarks>
public class Trigger_CerebrexDamageSinceBaseline : Trigger
{
    private readonly List<Thing> targets;

    private readonly float baselineHitPoints;

    private readonly float damageFraction;

    public Trigger_CerebrexDamageSinceBaseline(List<Thing> targets, float baselineHitPoints, float damageFraction)
    {
        this.targets = targets;
        this.baselineHitPoints = baselineHitPoints;
        this.damageFraction = damageFraction;
    }

    public override bool ActivateOn(Lord lord, TriggerSignal signal)
    {
        if (signal.type != TriggerSignalType.Tick)
        {
            return false;
        }

        if (targets == null || targets.Count == 0)
        {
            return true;
        }

        float currentHitPoints = 0f;
        float totalMaxHitPoints = 0f;
        int aliveTargets = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is not { Spawned: true })
            {
                continue;
            }

            currentHitPoints += targets[i].HitPoints;
            totalMaxHitPoints += targets[i].MaxHitPoints;
            aliveTargets++;
        }

        // Objective already gone - nothing left to assault.
        if (aliveTargets == 0)
        {
            return true;
        }

        return baselineHitPoints - currentHitPoints >= damageFraction * totalMaxHitPoints;
    }
}

/// <summary>
/// Assault graph focused on a fixed target list (the cerebrex core) rather than the colony at
/// large. Modelled on LordJob_AssaultColony.CreateGraph, with the kidnap/steal subgraphs dropped -
/// these raiders are here for one thing.
/// </summary>
public class LordJob_CerebrexAssault : LordJob, ILordAvoidTraps
{
    private static readonly IntRange AssaultTimeBeforeGiveUp = new(26000, 38000);

    private Faction assaulterFaction;

    private List<Thing> targets;

    private bool sappers;

    private bool useAvoidGridSmart;

    private float retreatFraction = 0.5f;

    // Rolled/captured once at construction and scribed, never recomputed inside CreateGraph.
    // CreateGraph re-runs on load, so recomputing either of these would hand a reloaded save a
    // different graph than the one it was saved under - and would reset the damage baseline,
    // making the retreat trigger unreachable by save-scumming.
    private int giveUpTicks;

    private float coreHpBaseline;

    public override bool GuiltyOnDowned => true;

    public float AvoidTrapRatio => useAvoidGridSmart ? 0.3f : 0f;

    public LordJob_CerebrexAssault()
    {
    }

    public LordJob_CerebrexAssault(Faction assaulterFaction, Thing target, bool sappers = false,
        bool useAvoidGridSmart = false, float retreatFraction = 0.5f, int giveUpTicks = -1,
        float coreHpBaseline = -1f)
        : this(assaulterFaction, new List<Thing> { target }, sappers, useAvoidGridSmart, retreatFraction,
            giveUpTicks, coreHpBaseline)
    {
    }

    public LordJob_CerebrexAssault(Faction assaulterFaction, List<Thing> targets, bool sappers = false,
        bool useAvoidGridSmart = false, float retreatFraction = 0.5f, int giveUpTicks = -1,
        float coreHpBaseline = -1f)
    {
        this.assaulterFaction = assaulterFaction;
        this.targets = targets;
        this.sappers = sappers;
        this.useAvoidGridSmart = useAvoidGridSmart;
        this.retreatFraction = retreatFraction;
        this.giveUpTicks = giveUpTicks > 0 ? giveUpTicks : AssaultTimeBeforeGiveUp.RandomInRange;
        this.coreHpBaseline = coreHpBaseline >= 0f ? coreHpBaseline : TotalHitPointsOf(targets);
    }

    public static int RollGiveUpTicks()
    {
        return AssaultTimeBeforeGiveUp.RandomInRange;
    }

    /// <summary>Snapshot used as the "before this raid" reference point for the retreat trigger.</summary>
    public static float TotalHitPointsOf(List<Thing> things)
    {
        if (things == null)
        {
            return 0f;
        }

        float total = 0f;
        for (int i = 0; i < things.Count; i++)
        {
            if (things[i] is { Spawned: true })
            {
                total += things[i].HitPoints;
            }
        }

        return total;
    }

    public override StateGraph CreateGraph()
    {
        StateGraph graph = new();
        List<LordToil> extraSources = new();

        LordToil sapperToil = null;
        if (sappers)
        {
            sapperToil = new LordToil_CerebrexSappers(targets);
            if (useAvoidGridSmart)
            {
                sapperToil.useAvoidGrid = true;
            }

            graph.AddToil(sapperToil);
            extraSources.Add(sapperToil);

            Transition reassign = new(sapperToil, sapperToil, canMoveToSameState: true);
            reassign.AddTrigger(new Trigger_PawnLost());
            graph.AddTransition(reassign);
        }

        LordToil assaultToil = new LordToil_AssaultThings(targets);
        if (useAvoidGridSmart)
        {
            assaultToil.useAvoidGrid = true;
        }

        graph.AddToil(assaultToil);

        LordToil_ExitMapAndDefendSelf exitToil = new() { useAvoidGrid = true };
        graph.AddToil(exitToil);

        if (sappers)
        {
            Transition sappersDone = new(sapperToil, assaultToil);
            sappersDone.AddTrigger(new Trigger_NoFightingSappers());
            graph.AddTransition(sappersDone);
        }

        // Enough damage inflicted during this raid: satisfied, withdraw.
        Transition objectiveMet = new(assaultToil, exitToil);
        objectiveMet.AddSources(extraSources);
        objectiveMet.AddTrigger(new Trigger_CerebrexDamageSinceBaseline(targets, coreHpBaseline, retreatFraction));
        AddLeavingMessage(objectiveMet);
        graph.AddTransition(objectiveMet);

        // Took too long: give up.
        Transition timedOut = new(assaultToil, exitToil);
        timedOut.AddSources(extraSources);
        timedOut.AddTrigger(new Trigger_TicksPassed(giveUpTicks).WithFilter(new TriggerFilter_MapExitable()));
        AddLeavingMessage(timedOut);
        graph.AddTransition(timedOut);

        if (assaulterFaction != null)
        {
            Transition peaceBrokeOut = new(assaultToil, exitToil);
            peaceBrokeOut.AddSources(extraSources);
            peaceBrokeOut.AddTrigger(new Trigger_BecameNonHostileToPlayer());
            AddLeavingMessage(peaceBrokeOut);
            graph.AddTransition(peaceBrokeOut);
        }

        return graph;
    }

    private void AddLeavingMessage(Transition transition)
    {
        if (assaulterFaction == null)
        {
            return;
        }

        transition.AddPreAction(new TransitionAction_Message(
            "MessageRaidersLeaving".Translate(assaulterFaction.def.pawnsPlural.CapitalizeFirst(), assaulterFaction.Name)));
    }

    // Every field CreateGraph() reads must be scribed. Lord/LordJob rebuild the graph from scratch
    // on load and only the current toil index is restored, so a missing field silently produces a
    // differently shaped graph than the saved index expects.
    public override void ExposeData()
    {
        Scribe_References.Look(ref assaulterFaction, "assaulterFaction");
        Scribe_Collections.Look(ref targets, "targets", LookMode.Reference);
        Scribe_Values.Look(ref sappers, "sappers", defaultValue: false);
        Scribe_Values.Look(ref useAvoidGridSmart, "useAvoidGridSmart", defaultValue: false);
        Scribe_Values.Look(ref retreatFraction, "retreatFraction", 0.5f);
        Scribe_Values.Look(ref giveUpTicks, "giveUpTicks", 0);
        Scribe_Values.Look(ref coreHpBaseline, "coreHpBaseline", 0f);

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            targets ??= new List<Thing>();
            targets.RemoveAll(t => t == null);
        }
    }
}
