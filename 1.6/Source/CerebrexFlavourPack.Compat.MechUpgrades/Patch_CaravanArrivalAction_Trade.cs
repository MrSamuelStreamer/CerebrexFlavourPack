using HarmonyLib;
using RimWorld;
using RimWorld.Planet;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

[HarmonyPatch(typeof(CaravanArrivalAction_Trade), nameof(CaravanArrivalAction_Trade.CanTradeWith))]
public static class Patch_CaravanArrivalAction_Trade
{
    public static void Postfix(Caravan caravan, Settlement settlement, ref FloatMenuAcceptanceReport __result)
    {
        if (__result.Accepted)
        {
            return;
        }

        if (settlement == null || !settlement.Spawned || settlement.HasMap)
        {
            return;
        }

        Faction faction = settlement.Faction;
        if (faction == null || faction == Faction.OfPlayer)
        {
            return;
        }

        bool blockedByHostility = faction.HostileTo(Faction.OfPlayer) || faction.def.permanentEnemy;
        if (!blockedByHostility)
        {
            return;
        }

        UpgradeComp_HostileTrade relay = HostileTradeUtility.RelayInCaravan(caravan);
        if (relay == null)
        {
            return;
        }

        if (faction.def.permanentEnemy && !relay.Props.ignorePermanentEnemy)
        {
            return;
        }

        // CanTradeNow is false for pirates etc. that have no trader kinds.
        if (!settlement.CanTradeNow || !CaravanArrivalAction_Trade.HasNegotiator(caravan, settlement))
        {
            return;
        }

        __result = true;
    }
}
