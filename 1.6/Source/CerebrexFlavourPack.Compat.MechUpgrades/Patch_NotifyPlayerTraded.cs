using HarmonyLib;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

// Stop hostile trades from buying goodwill back to neutral.
[HarmonyPatch(typeof(Faction), nameof(Faction.Notify_PlayerTraded))]
public static class Patch_NotifyPlayerTraded
{
    public static bool Prefix(Faction __instance)
    {
        Faction player = Faction.OfPlayer;
        return player == null || !__instance.HostileTo(player);
    }
}
