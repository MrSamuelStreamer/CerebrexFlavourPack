using RimWorld;
using Verse;

namespace CerebrexFlavourPack.Compat.Playwright;

[DefOf]
public static class CFPCompatPlaywrightDefOf
{
    public static ThingDef CFP_DelayedSapientAnimalSpawnerWithHediff;

    static CFPCompatPlaywrightDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CFPCompatPlaywrightDefOf));
}
