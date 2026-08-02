using System.Collections.Generic;
using MU;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

public static class HostileTradeUtility
{
    public static UpgradeComp_HostileTrade RelayOn(Pawn pawn)
    {
        if (pawn == null || pawn.Dead || pawn.Downed)
        {
            return null;
        }

        List<MechUpgrade> upgrades = pawn.TryGetComp<CompUpgradableMechanoid>()?.upgrades;
        if (upgrades == null)
        {
            return null;
        }

        for (int i = 0; i < upgrades.Count; i++)
        {
            if (upgrades[i] is not MechUpgradeWithComps withComps)
            {
                continue;
            }

            UpgradeComp_HostileTrade relay = withComps.GetComp<UpgradeComp_HostileTrade>();
            if (relay != null)
            {
                return relay;
            }
        }

        return null;
    }

    public static UpgradeComp_HostileTrade RelayInCaravan(Caravan caravan)
    {
        if (caravan == null)
        {
            return null;
        }

        List<Pawn> pawns = caravan.PawnsListForReading;
        for (int i = 0; i < pawns.Count; i++)
        {
            UpgradeComp_HostileTrade relay = RelayOn(pawns[i]);
            if (relay != null)
            {
                return relay;
            }
        }

        return null;
    }

    public static UpgradeComp_HostileTrade RelayOnMap(Map map)
    {
        if (map == null)
        {
            return null;
        }

        List<Pawn> pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
        for (int i = 0; i < pawns.Count; i++)
        {
            UpgradeComp_HostileTrade relay = RelayOn(pawns[i]);
            if (relay != null)
            {
                return relay;
            }
        }

        return null;
    }

    public static UpgradeComp_HostileTrade RelayFor(Pawn negotiator)
    {
        if (negotiator == null)
        {
            return null;
        }

        UpgradeComp_HostileTrade relay = RelayOn(negotiator);
        if (relay != null)
        {
            return relay;
        }

        Caravan caravan = negotiator.GetCaravan();
        return caravan != null ? RelayInCaravan(caravan) : RelayOnMap(negotiator.MapHeld);
    }
}
