using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

[DefOf]
public static class CerebrexFlavourPackDefOf
{
    // Odyssey is a hard dependency (see About.xml), so none of these need [MayRequire] guards.
    public static ThingDef CFP_CerebrexCore;

    public static RaidStrategyDef CFP_CerebrexStrike;

    public static RaidStrategyDef CFP_CerebrexStrikeSappers;

    public static RaidStrategyDef CFP_CerebrexSiege;

    public static ResearchProjectDef CFP_CerebrexIntegration;

    public static HediffDef CFP_CerebrexShutdown;

    public static JobDef CFP_CarryPawnToPrisonerProcessor;

    /// <summary>Permanent world condition that replaces the home system's star with a black hole.</summary>
    public static GameConditionDef CFP_BlackHole;

    static CerebrexFlavourPackDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(CerebrexFlavourPackDefOf));
}
