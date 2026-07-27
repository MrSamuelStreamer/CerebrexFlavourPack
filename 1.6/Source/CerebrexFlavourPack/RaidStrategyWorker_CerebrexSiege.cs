using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace CerebrexFlavourPack;

public class RaidStrategyWorker_CerebrexSiege : RaidStrategyWorker_CerebrexBase
{
    /// <summary>How close the siege camp must sit to the core for the bombardment to matter.</summary>
    private const int MaxSiegeDistanceFromCore = 40;

    public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
    {
        if (!base.CanUseWith(parms, groupKind))
        {
            return false;
        }

        return parms.faction.def.canSiege;
    }

    public override bool CanUsePawnGenOption(float pointsTotal, PawnGenOption g, List<PawnGenOptionWithXenotype> chosenGroups, Faction faction = null)
    {
        if (g.kind.RaceProps.Animal)
        {
            return false;
        }

        return base.CanUsePawnGenOption(pointsTotal, g, chosenGroups, faction);
    }

    protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
    {
        Building core = FindCore(map);
        IntVec3 entrySpot = parms.spawnCenter.IsValid ? parms.spawnCenter : pawns[0].PositionHeld;
        float blueprintPoints = Mathf.Max(60f, parms.points * Rand.Range(0.2f, 0.3f));

        if (core == null)
        {
            return new LordJob_Siege(parms.faction, RCellFinder.FindSiegePositionFrom(entrySpot, map), blueprintPoints);
        }

        IntVec3 siegeSpot = FindSiegeSpotNearCore(entrySpot, map, core);
        return new LordJob_CerebrexSiege(parms.faction, siegeSpot, blueprintPoints, core, RetreatFraction());
    }

    /// <summary>
    /// FindSiegePositionFrom never returns an invalid cell - on failure it falls back to entrySpot.
    /// So try the core-constrained search first, then detect that fallback and retry unconstrained.
    /// </summary>
    private static IntVec3 FindSiegeSpotNearCore(IntVec3 entrySpot, Map map, Building core)
    {
        int maxDistSquared = MaxSiegeDistanceFromCore * MaxSiegeDistanceFromCore;
        bool NearCore(IntVec3 c) => c.DistanceToSquared(core.Position) <= maxDistSquared;

        IntVec3 constrained = RCellFinder.FindSiegePositionFrom(entrySpot, map, errorOnFail: false, validator: NearCore);
        if (NearCore(constrained))
        {
            return constrained;
        }

        return RCellFinder.FindSiegePositionFrom(entrySpot, map);
    }
}
