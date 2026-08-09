using HarmonyLib;
using Verse;

namespace CerebrexFlavourPack
{
    /// <summary>
    /// Hides the vanilla purple pollution cloud overlay when the user opts in via mod settings.
    /// Postfixes the SectionLayer_PollutionCloud.Visible getter — the base MapDrawer skips
    /// mesh drawing when Visible is false, so no forced redraw is required on toggle.
    /// </summary>
    [HarmonyPatch(typeof(SectionLayer_PollutionCloud), nameof(SectionLayer_PollutionCloud.Visible), MethodType.Getter)]
    public static class Patch_HidePollutionCloud
    {
        public static void Postfix(ref bool __result)
        {
            if (CerebrexFlavourPackMod.settings != null && CerebrexFlavourPackMod.settings.disablePollutionCloud)
            {
                __result = false;
            }
        }
    }
}
