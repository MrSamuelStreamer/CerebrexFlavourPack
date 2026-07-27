using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace CerebrexFlavourPack;

/// <summary>
/// Sapper variant. Descends from <see cref="RaidStrategyWorker_WithRequiredPawnKinds"/> (so the
/// group generator guarantees sapper-capable pawns) and composes the core-present gate from
/// <see cref="RaidStrategyWorker_CerebrexBase"/> rather than inheriting it.
/// </summary>
public class RaidStrategyWorker_CerebrexStrikeSappers : RaidStrategyWorker_WithRequiredPawnKinds
{
    protected override bool MatchesRequiredPawnKind(PawnKindDef kind)
    {
        return kind.canBeSapper;
    }

    protected override int MinRequiredPawnsForPoints(float pointsTotal, Faction faction = null)
    {
        return 1;
    }

    public override float SelectionWeight(Map map, float basePoints)
    {
        return RaidStrategyWorker_CerebrexBase.ScaledSelectionWeight(base.SelectionWeight(map, basePoints));
    }

    public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
    {
        // The parent returns false when no PawnGroupMaker for this group kind offers a sapper-capable
        // kind, so factions without sappers silently never roll this. That is intended.
        if (!base.CanUseWith(parms, groupKind))
        {
            return false;
        }

        return RaidStrategyWorker_CerebrexBase.FindCore(parms.target as Map) != null;
    }

    public override void TryGenerateThreats(IncidentParms parms)
    {
        base.TryGenerateThreats(parms);
        RaidStrategyWorker_CerebrexBase.AddCoreTarget(parms);
    }

    // Mirrors RaidStrategyWorker_ImmediateAttackSappers.CanUsePawn.
    public override bool CanUsePawn(float pointsTotal, Pawn p, List<Pawn> otherPawns)
    {
        if (otherPawns.Count == 0 && !SappersUtility.IsGoodSapper(p) && !SappersUtility.IsGoodBackupSapper(p))
        {
            return false;
        }

        if (p.kindDef.canBeSapper && SappersUtility.HasBuildingDestroyerWeapon(p) && !SappersUtility.IsGoodSapper(p))
        {
            return false;
        }

        return true;
    }

    protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
    {
        Building core = RaidStrategyWorker_CerebrexBase.FindCore(map);
        if (core == null)
        {
            return new LordJob_AssaultColony(parms.faction, canKidnap: parms.canKidnap,
                canTimeoutOrFlee: parms.canTimeoutOrFlee, sappers: true, useAvoidGridSmart: true,
                canSteal: parms.canSteal);
        }

        return new LordJob_CerebrexAssault(parms.faction, core, sappers: true, useAvoidGridSmart: true,
            retreatFraction: RaidStrategyWorker_CerebrexBase.RetreatFraction());
    }
}
