using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

// Vanilla SettlementVisitedNow requires Settlement.Visitable, which is false for hostiles.
[HarmonyPatch(typeof(CaravanVisitUtility), nameof(CaravanVisitUtility.SettlementVisitedNow))]
public static class Patch_SettlementVisitedNow
{
    public static void Postfix(Caravan caravan, ref Settlement __result)
    {
        if (__result != null || caravan == null || !caravan.Spawned)
        {
            return;
        }

        if (caravan.pather == null || caravan.pather.Moving)
        {
            return;
        }

        UpgradeComp_HostileTrade relay = HostileTradeUtility.RelayInCaravan(caravan);
        if (relay == null)
        {
            return;
        }

        if (Find.WorldObjects == null)
        {
            return;
        }

        List<Settlement> settlementBases = Find.WorldObjects.SettlementBases;
        for (int i = 0; i < settlementBases.Count; i++)
        {
            Settlement settlement = settlementBases[i];
            if (settlement.Tile != caravan.Tile || settlement.Faction == caravan.Faction)
            {
                continue;
            }

            Faction faction = settlement.Faction;
            if (faction == null || faction == Faction.OfPlayer)
            {
                continue;
            }

            if (!faction.HostileTo(Faction.OfPlayer) && !faction.def.permanentEnemy)
            {
                continue;
            }

            if (faction.def.permanentEnemy && !relay.Props.ignorePermanentEnemy)
            {
                continue;
            }

            if (settlement.Tile.LayerDef != null && settlement.Tile.LayerDef.isSpace)
            {
                continue;
            }

            __result = settlement;
            return;
        }
    }
}
