using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

public class Settings : ModSettings
{
    //Use Mod.settings.setting to refer to this setting.
    public bool setting = true;

    /// <summary>Multiplier on the Refurbish gizmo's plasteel/component cost per point of healing.</summary>
    public float refurbishCostRate = 1f;

    /// <summary>Fraction of the core's max HP that must be chewed off before a core raid withdraws.</summary>
    public float coreRetreatHpFraction = 0.5f;

    /// <summary>Multiplier on how often the storyteller picks a cerebrex core strategy. 0 disables them.</summary>
    public float strategySelectionWeight = 1f;

    public void DoWindowContents(Rect wrect)
    {
        Listing_Standard options = new();
        options.Begin(wrect);

        options.CheckboxLabeled("CerebrexFlavourPack_Settings_SettingName".Translate(), ref setting);
        options.Gap();

        refurbishCostRate = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_RefurbishCostRate".Translate(refurbishCostRate.ToString("0.##")),
            refurbishCostRate, 0f, 5f);
        options.Gap();

        coreRetreatHpFraction = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_CoreRetreatHpFraction".Translate(coreRetreatHpFraction.ToStringPercent()),
            coreRetreatHpFraction, 0.05f, 1f);
        options.Gap();

        strategySelectionWeight = options.SliderLabeled(
            "CerebrexFlavourPack_Settings_StrategySelectionWeight".Translate(strategySelectionWeight.ToString("0.##")),
            strategySelectionWeight, 0f, 5f);
        options.Gap();

        options.End();
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref setting, "setting", true);
        Scribe_Values.Look(ref refurbishCostRate, "refurbishCostRate", 1f);
        Scribe_Values.Look(ref coreRetreatHpFraction, "coreRetreatHpFraction", 0.5f);
        Scribe_Values.Look(ref strategySelectionWeight, "strategySelectionWeight", 1f);
    }
}
