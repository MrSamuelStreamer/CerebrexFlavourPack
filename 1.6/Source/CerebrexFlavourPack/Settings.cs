using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

public class Settings : ModSettings
{
    /// <summary>Multiplier on the Refurbish gizmo's plasteel/component cost per point of healing.</summary>
    public float refurbishCostRate = 1f;

    /// <summary>Fraction of the core's max HP that must be chewed off before a core raid withdraws.</summary>
    public float coreRetreatHpFraction = 0.5f;

    /// <summary>Multiplier on how often the storyteller picks a cerebrex core strategy. 0 disables them.</summary>
    public float strategySelectionWeight = 1f;

    /// <summary>Permanently replaces the home system's star with a black hole. On by default.</summary>
    public bool blackHoleSunEnabled = true;

    /// <summary>Fraction of normal sunlight the black hole lets through on the map (SkyTarget glow cap).</summary>
    public float blackHoleLightCap = 0.25f;

    public void DoWindowContents(Rect wrect)
    {
        Listing_Standard options = new();
        options.Begin(wrect);

        bool blackHolePrev = blackHoleSunEnabled;
        options.CheckboxLabeled(
            "CerebrexFlavourPack_Settings_BlackHoleSunEnabled".Translate(), ref blackHoleSunEnabled,
            "CerebrexFlavourPack_Settings_BlackHoleSunEnabled_Tooltip".Translate());
        if (blackHoleSunEnabled != blackHolePrev)
            WorldComponent_CerebrexBlackHole.SyncActiveStateWithSetting();
        options.Gap();

        blackHoleLightCap = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_BlackHoleLightCap".Translate(blackHoleLightCap.ToStringPercent()),
            blackHoleLightCap, 0.05f, 1f,
            tooltip: "CerebrexFlavourPack_Settings_BlackHoleLightCap_Tooltip".Translate());
        options.Gap();

        refurbishCostRate = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_RefurbishCostRate".Translate(refurbishCostRate.ToString("0.##")),
            refurbishCostRate, 0f, 5f,
            tooltip: "CerebrexFlavourPack_Settings_RefurbishCostRate_Tooltip".Translate());
        options.Gap();

        coreRetreatHpFraction = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_CoreRetreatHpFraction".Translate(coreRetreatHpFraction.ToStringPercent()),
            coreRetreatHpFraction, 0.05f, 1f,
            tooltip: "CerebrexFlavourPack_Settings_CoreRetreatHpFraction_Tooltip".Translate());
        options.Gap();

        strategySelectionWeight = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_StrategySelectionWeight".Translate(strategySelectionWeight.ToString("0.##")),
            strategySelectionWeight, 0f, 5f,
            tooltip: "CerebrexFlavourPack_Settings_StrategySelectionWeight_Tooltip".Translate());
        options.Gap();

        options.End();
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref refurbishCostRate, "refurbishCostRate", 1f);
        Scribe_Values.Look(ref coreRetreatHpFraction, "coreRetreatHpFraction", 0.5f);
        Scribe_Values.Look(ref strategySelectionWeight, "strategySelectionWeight", 1f);
        Scribe_Values.Look(ref blackHoleSunEnabled, "blackHoleSunEnabled", true);
        Scribe_Values.Look(ref blackHoleLightCap, "blackHoleLightCap", 0.75f);
    }
}
