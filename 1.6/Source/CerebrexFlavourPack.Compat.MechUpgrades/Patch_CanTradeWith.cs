using HarmonyLib;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

[HarmonyPatch(typeof(FactionUtility), nameof(FactionUtility.CanTradeWith))]
public static class Patch_CanTradeWith
{
    public static void Postfix(Pawn p, Faction faction, TraderKindDef traderKind, ref AcceptanceReport __result)
    {
        if (__result.Accepted || p == null || faction == null)
        {
            return;
        }

        if (!faction.HostileTo(p.Faction))
        {
            return;
        }

        // HasNegotiator / TradeCommand dereference skills with no null check.
        if (p.skills == null || p.skills.GetSkill(SkillDefOf.Social).TotallyDisabled)
        {
            return;
        }

        if (HostileTradeUtility.RelayFor(p) == null)
        {
            return;
        }

        if (traderKind?.permitRequiredForTrading != null
            && (p.royalty == null || !p.royalty.HasPermit(traderKind.permitRequiredForTrading, faction)))
        {
            return;
        }

        __result = AcceptanceReport.WasAccepted;
    }
}
