using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Shared behaviour for every cerebrex core strategy: gate on a player-owned core existing on the
/// map being raided, and point <see cref="IncidentParms.attackTargets"/> at it so the arrival-mode
/// worker positions the raid relative to the core.
/// </summary>
/// <remarks>
/// <see cref="FindCore"/> and <see cref="AddCoreTarget"/> are public statics rather than protected
/// instance members because RaidStrategyWorker_CerebrexStrikeSappers has to descend from
/// <see cref="RaidStrategyWorker_WithRequiredPawnKinds"/> and therefore composes this behaviour
/// instead of inheriting it.
/// </remarks>
public abstract class RaidStrategyWorker_CerebrexBase : RaidStrategyWorker
{
    /// <summary>Cerebrex core on <paramref name="map"/> owned by the player, or null.</summary>
    public static Building FindCore(Map map)
    {
        if (map == null)
        {
            return null;
        }

        // AllBuildingsColonistOfDef hands back a shared reused buffer - copy the element out
        // immediately and never hold on to the list itself.
        List<Building> sharedBuffer = map.listerBuildings.AllBuildingsColonistOfDef(CerebrexFlavourPackDefOf.CFP_CerebrexCore);
        return sharedBuffer.Count > 0 ? sharedBuffer[0] : null;
    }

    /// <summary>
    /// Runs immediately before PawnsArrivalModeWorker.TryResolveRaidSpawnCenter, which is what lets
    /// EdgeWalkIn pick an edge cell whose approach to the core avoids the colony.
    /// </summary>
    public static void AddCoreTarget(IncidentParms parms)
    {
        Building core = FindCore(parms.target as Map);
        if (core == null)
        {
            return;
        }

        parms.attackTargets ??= new List<Thing>();
        if (!parms.attackTargets.Contains(core))
        {
            parms.attackTargets.Add(core);
        }
    }

    public static float ScaledSelectionWeight(float baseWeight)
    {
        return baseWeight * (CerebrexFlavourPackMod.settings?.strategySelectionWeight ?? 1f);
    }

    public static float RetreatFraction()
    {
        return CerebrexFlavourPackMod.settings?.coreRetreatHpFraction ?? 0.5f;
    }

    public override float SelectionWeight(Map map, float basePoints)
    {
        return ScaledSelectionWeight(base.SelectionWeight(map, basePoints));
    }

    public override bool CanUseWith(IncidentParms parms, PawnGroupKindDef groupKind)
    {
        if (!base.CanUseWith(parms, groupKind))
        {
            return false;
        }

        return FindCore(parms.target as Map) != null;
    }

    public override void TryGenerateThreats(IncidentParms parms)
    {
        base.TryGenerateThreats(parms);
        AddCoreTarget(parms);
    }
}
