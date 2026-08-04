using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace CerebrexFlavourPack.Compat.SapientAnimals;

/// <summary>
/// These patches fix steel storage init, Release UI, CanSpawn, and haul-to-carrier jobs.
/// </summary>
[HarmonyPatch(typeof(CompMechCarrier), nameof(CompMechCarrier.PostSpawnSetup))]
public static class Patch_SapientMechCarrier_PostSpawnSetup
{
    public static void Postfix(CompMechCarrier __instance)
    {
        if (SapientMechCarrierUtility.IsSapientPlayerCarrier(__instance))
            SapientMechCarrierUtility.EnsureInnerContainer(__instance);
    }
}

[HarmonyPatch(typeof(CompMechCarrier), nameof(CompMechCarrier.CompInspectStringExtra))]
public static class Patch_SapientMechCarrier_InspectString
{
    public static void Prefix(CompMechCarrier __instance)
    {
        if (SapientMechCarrierUtility.IsSapientPlayerCarrier(__instance))
            SapientMechCarrierUtility.EnsureInnerContainer(__instance);
    }
}

[HarmonyPatch(typeof(CompMechCarrier), "get_CanSpawn")]
public static class Patch_SapientMechCarrier_CanSpawn
{
    public static bool Prefix(CompMechCarrier __instance, ref AcceptanceReport __result)
    {
        if (!SapientMechCarrierUtility.IsSapientPlayerCarrier(__instance))
            return true;

        __result = SapientMechCarrierUtility.GetSapientCanSpawn(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(CompMechCarrier), nameof(CompMechCarrier.CompGetGizmosExtra))]
public static class Patch_SapientMechCarrier_Gizmos
{
    public static bool Prefix(CompMechCarrier __instance, ref IEnumerable<Gizmo> __result)
    {
        if (!SapientMechCarrierUtility.IsSapientPlayerCarrier(__instance))
            return true;

        __result = SapientMechCarrierUtility.GetSapientCarrierGizmos(__instance);
        return false;
    }
}

[HarmonyPatch(typeof(WorkGiver_HaulResourcesToCarrier), nameof(WorkGiver_HaulResourcesToCarrier.HasJobOnThing))]
public static class Patch_SapientMechCarrier_HaulJob
{
    public static void Postfix(Pawn pawn, Thing t, bool forced, ref bool __result)
    {
        if (__result || !ModLister.BiotechInstalled)
            return;

        if (t is not Pawn carrier || !SapientMechCarrierUtility.IsSapientPlayerCarrier(carrier))
            return;
        if (!carrier.Spawned || carrier.Downed)
            return;

        CompMechCarrier comp = carrier.GetComp<CompMechCarrier>();
        if (comp == null)
            return;

        SapientMechCarrierUtility.EnsureInnerContainer(comp);
        int amountToAutofill = comp.AmountToAutofill;
        __result = amountToAutofill > 0
                   && pawn.CanReserve(t, 1, -1, null, forced)
                   && !HaulAIUtility.FindFixedIngredientCount(pawn, comp.Props.fixedIngredient, amountToAutofill)
                       .NullOrEmpty();
    }
}
