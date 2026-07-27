using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Sapper toil that digs toward the cerebrex core instead of GenAI.RandomRaidDest.
/// </summary>
/// <remarks>
/// Reuses <see cref="LordToilData_AssaultColonySappers"/> unchanged - LordToil.data is public, so
/// no new LordToilData subclass and no extra ExposeData is needed. The targets list is supplied
/// fresh by LordJob_CerebrexAssault.CreateGraph() on every load, so it does not need scribing here.
/// </remarks>
public class LordToil_CerebrexSappers : LordToil_AssaultColonySappers
{
    private readonly List<Thing> targets;

    public LordToil_CerebrexSappers(List<Thing> targets)
    {
        this.targets = targets;
    }

    public override void UpdateAllDuties()
    {
        // The base only assigns sapperDest when it is invalid, so forcing it here wins.
        if (data is LordToilData_AssaultColonySappers sapperData)
        {
            sapperData.sapperDest = SapperDestination();
        }

        base.UpdateAllDuties();
    }

    private IntVec3 SapperDestination()
    {
        Thing target = FirstSpawnedTarget();
        if (target == null)
        {
            return IntVec3.Invalid;
        }

        IntVec3 origin = lord.ownedPawns.Count > 0 ? lord.ownedPawns[0].PositionHeld : target.Position;
        return ApproachCellFor(target, origin);
    }

    private Thing FirstSpawnedTarget()
    {
        if (targets == null)
        {
            return null;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] is { Spawned: true })
            {
                return targets[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Never returns the core's own Position. The core is 7x7 and Impassable, so its centre cell has
    /// no room and JobGiver_AISapper's arrival check (intVec.GetRoom(map) == pawn.GetRoom()) would
    /// never fire, looping the sappers forever. A ring cell one out from the occupied rect does have
    /// a room once reached, and dropping the Sapper duty there hands off to JobGiver_AITrashColonyClose.
    /// </summary>
    private static IntVec3 ApproachCellFor(Thing target, IntVec3 origin)
    {
        Map map = target.Map;
        if (map == null)
        {
            return target.Position;
        }

        IntVec3 bestWalkable = IntVec3.Invalid;
        int bestWalkableDist = int.MaxValue;
        IntVec3 bestAny = IntVec3.Invalid;
        int bestAnyDist = int.MaxValue;

        foreach (IntVec3 cell in target.OccupiedRect().ExpandedBy(1).EdgeCells)
        {
            if (!cell.InBounds(map))
            {
                continue;
            }

            int dist = cell.DistanceToSquared(origin);
            if (dist < bestAnyDist)
            {
                bestAnyDist = dist;
                bestAny = cell;
            }

            if (cell.Walkable(map) && dist < bestWalkableDist)
            {
                bestWalkableDist = dist;
                bestWalkable = cell;
            }
        }

        if (bestWalkable.IsValid)
        {
            return bestWalkable;
        }

        // Fully walled in: aim at the nearest ring cell anyway. It is a destroyable wall, so the
        // sapper pathfinder (TraverseMode.PassAllDestroyableThings) will happily dig to it.
        return bestAny.IsValid ? bestAny : target.Position;
    }
}
