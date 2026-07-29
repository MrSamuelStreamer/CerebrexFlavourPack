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

    /// <summary>Whether the black hole's apparent size grows as it ages.</summary>
    public bool blackHoleGrowthEnabled = true;

    /// <summary>On-screen area doublings per in-game year. 1.0 = area doubles annually.</summary>
    public float blackHoleGrowthRate = 1f;

    /// <summary>Ceiling on the radius growth multiplier, so the disc never fills the world map.</summary>
    public float blackHoleGrowthMax = 16f;

    /// <summary>Master switch for the cerebrex lattice spread/biome-flip/mood feature (requires Alpha Biomes).</summary>
    public bool latticeEnabled = true;

    /// <summary>Steel cost to convert one map cell to lattice terrain.</summary>
    public float latticeSteelPerCell = 1f;

    /// <summary>Absolute steel stockpile floor below which the lattice stops feeding.</summary>
    public float latticeSteelReserve = 200f;

    /// <summary>In-game days a constant steel stream takes to cover the whole map.</summary>
    public float latticeTargetDays = 60f;

    /// <summary>Fraction of cell placements spent on branching DLA growth vs. thickening fill.</summary>
    public float latticeDlaFraction = 0.10f;

    /// <summary>Chance, per DLA particle spawned, that it becomes a long-range runner island instead.</summary>
    public float latticeRunnerChance = 0.025f;

    /// <summary>Coverage fraction (converted/convertible) at which the world tile's biome permanently flips.</summary>
    public float biomeFlipThreshold = 0.75f;

    /// <summary>Multiplier on the mechanoid-terrain mood thought. 0 disables the thought entirely.</summary>
    public float moodBuffScale = 1f;

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

        options.CheckboxLabeled(
            "CerebrexFlavourPack_Settings_BlackHoleGrowthEnabled".Translate(), ref blackHoleGrowthEnabled,
            "CerebrexFlavourPack_Settings_BlackHoleGrowthEnabled_Tooltip".Translate());
        options.Gap();

        if (blackHoleGrowthEnabled)
        {
            blackHoleGrowthRate = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_BlackHoleGrowthRate".Translate(blackHoleGrowthRate.ToString("0.##")),
                blackHoleGrowthRate, 0f, 5f,
                tooltip: "CerebrexFlavourPack_Settings_BlackHoleGrowthRate_Tooltip".Translate());
            options.Gap();

            blackHoleGrowthMax = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_BlackHoleGrowthMax".Translate(blackHoleGrowthMax.ToString("0.#")),
                blackHoleGrowthMax, 1f, 32f,
                tooltip: "CerebrexFlavourPack_Settings_BlackHoleGrowthMax_Tooltip".Translate());
            options.Gap();
        }

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

        if (CerebrexLatticeDefs.Active)
        {
            options.CheckboxLabeled(
                "CerebrexFlavourPack_Settings_LatticeEnabled".Translate(), ref latticeEnabled,
                "CerebrexFlavourPack_Settings_LatticeEnabled_Tooltip".Translate());
            options.Gap();

            latticeSteelPerCell = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_LatticeSteelPerCell".Translate(latticeSteelPerCell.ToString("0.##")),
                latticeSteelPerCell, 0.25f, 5f,
                tooltip: "CerebrexFlavourPack_Settings_LatticeSteelPerCell_Tooltip".Translate());
            options.Gap();

            latticeSteelReserve = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_LatticeSteelReserve".Translate(latticeSteelReserve.ToString("0")),
                latticeSteelReserve, 0f, 2000f,
                tooltip: "CerebrexFlavourPack_Settings_LatticeSteelReserve_Tooltip".Translate());
            options.Gap();

            latticeTargetDays = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_LatticeTargetDays".Translate(latticeTargetDays.ToString("0")),
                latticeTargetDays, 10f, 240f,
                tooltip: "CerebrexFlavourPack_Settings_LatticeTargetDays_Tooltip".Translate());
            options.Gap();

            latticeDlaFraction = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_LatticeDlaFraction".Translate(latticeDlaFraction.ToStringPercent()),
                latticeDlaFraction, 0f, 0.5f,
                tooltip: "CerebrexFlavourPack_Settings_LatticeDlaFraction_Tooltip".Translate());
            options.Gap();

            latticeRunnerChance = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_LatticeRunnerChance".Translate(latticeRunnerChance.ToStringPercent()),
                latticeRunnerChance, 0f, 0.15f,
                tooltip: "CerebrexFlavourPack_Settings_LatticeRunnerChance_Tooltip".Translate());
            options.Gap();

            biomeFlipThreshold = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_BiomeFlipThreshold".Translate(biomeFlipThreshold.ToStringPercent()),
                biomeFlipThreshold, 0.25f, 1f,
                tooltip: "CerebrexFlavourPack_Settings_BiomeFlipThreshold_Tooltip".Translate());
            options.Gap();

            moodBuffScale = options.SliderLabeled(
                "CerebrexFlavourPack_Settings_MoodBuffScale".Translate(moodBuffScale.ToString("0.##")),
                moodBuffScale, 0f, 2f,
                tooltip: "CerebrexFlavourPack_Settings_MoodBuffScale_Tooltip".Translate());
            options.Gap();
        }

        options.End();
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref refurbishCostRate, "refurbishCostRate", 1f);
        Scribe_Values.Look(ref coreRetreatHpFraction, "coreRetreatHpFraction", 0.5f);
        Scribe_Values.Look(ref strategySelectionWeight, "strategySelectionWeight", 1f);
        Scribe_Values.Look(ref blackHoleSunEnabled, "blackHoleSunEnabled", true);
        Scribe_Values.Look(ref blackHoleLightCap, "blackHoleLightCap", 0.75f);
        Scribe_Values.Look(ref blackHoleGrowthEnabled, "blackHoleGrowthEnabled", true);
        Scribe_Values.Look(ref blackHoleGrowthRate, "blackHoleGrowthRate", 1f);
        Scribe_Values.Look(ref blackHoleGrowthMax, "blackHoleGrowthMax", 16f);
        Scribe_Values.Look(ref latticeEnabled, "latticeEnabled", true);
        Scribe_Values.Look(ref latticeSteelPerCell, "latticeSteelPerCell", 1f);
        Scribe_Values.Look(ref latticeSteelReserve, "latticeSteelReserve", 200f);
        Scribe_Values.Look(ref latticeTargetDays, "latticeTargetDays", 60f);
        Scribe_Values.Look(ref latticeDlaFraction, "latticeDlaFraction", 0.10f);
        Scribe_Values.Look(ref latticeRunnerChance, "latticeRunnerChance", 0.025f);
        Scribe_Values.Look(ref biomeFlipThreshold, "biomeFlipThreshold", 0.75f);
        Scribe_Values.Look(ref moodBuffScale, "moodBuffScale", 1f);
    }
}
