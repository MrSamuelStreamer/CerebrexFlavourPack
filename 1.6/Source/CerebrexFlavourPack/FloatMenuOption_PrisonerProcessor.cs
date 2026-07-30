using RimWorld;
using Verse;
using Verse.AI;

namespace CerebrexFlavourPack;

public class FloatMenuOptionProvider_PrisonerProcessor : FloatMenuOptionProvider
{


    protected override bool Drafted => true;
    protected override bool Undrafted => true;
    protected override bool Multiselect => false;
    protected override bool RequiresManipulation => true;
    protected override FloatMenuOption GetSingleOptionFor(Pawn clickedPawn, FloatMenuContext context)
    {
        Pawn actor = context.FirstSelectedPawn;

        if (!actor.IsColonistPlayerControlled)
            return null;
        if (clickedPawn == actor)
            return null;
        if (!clickedPawn.RaceProps.Humanlike || clickedPawn.Dead || !clickedPawn.IsPrisonerOfColony)
            return null;

        string label = $"Assign {clickedPawn.LabelShortCap} to processing";


        if (!actor.CanReserveAndReach(clickedPawn, PathEndMode.Touch, Danger.Deadly))
            return new FloatMenuOption($"{label} (no path or reserved)", null);

        Thing processor = ClosestAvailableProcessor(actor, clickedPawn);
        if (processor == null)
            return new FloatMenuOption($"{label} (no powered empty processor reachable)", null);

        void Action()
        {
            Job job = JobMaker.MakeJob(CerebrexFlavourPackDefOf.CFP_CarryPawnToPrisonerProcessor, clickedPawn, processor);
            job.count = 1;
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        return FloatMenuUtility.DecoratePrioritizedTask(new FloatMenuOption(label, Action), actor, clickedPawn);
    }


    private static Thing ClosestAvailableProcessor(Pawn actor, Pawn target)
    {
        Thing bestThing = null;
        float bestDistanceSquared = float.MaxValue;

        PrisonerProcessorTracker tracker = actor.Map.GetComponent<PrisonerProcessorTracker>();
        if (tracker == null)
            return null;

        foreach (CompPrisonerProcessor comp in tracker.Processors)
        {
            if (!comp.CanAcceptPawn(target))
                continue;
            Thing thing = comp.ParentThing;
            if (!actor.CanReserveAndReach(thing, PathEndMode.InteractionCell, Danger.Deadly))
                continue;

            float distanceSquared = actor.Position.DistanceToSquared(thing.Position);
            if (distanceSquared >= bestDistanceSquared)
                continue;
            bestThing = thing;
            bestDistanceSquared = distanceSquared;
        }
        return bestThing;
    }
}
