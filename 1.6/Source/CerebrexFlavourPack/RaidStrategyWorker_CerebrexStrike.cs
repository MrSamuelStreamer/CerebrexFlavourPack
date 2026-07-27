using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace CerebrexFlavourPack;

public class RaidStrategyWorker_CerebrexStrike : RaidStrategyWorker_CerebrexBase
{
    protected override LordJob MakeLordJob(IncidentParms parms, Map map, List<Pawn> pawns, int raidSeed)
    {
        Building core = FindCore(map);
        if (core == null)
        {
            // The core can be destroyed between strategy resolution and lord creation.
            return new LordJob_AssaultColony(parms.faction, canKidnap: parms.canKidnap,
                canTimeoutOrFlee: parms.canTimeoutOrFlee, canSteal: parms.canSteal);
        }

        return new LordJob_CerebrexAssault(parms.faction, core, sappers: false, useAvoidGridSmart: true,
            retreatFraction: RetreatFraction());
    }
}
