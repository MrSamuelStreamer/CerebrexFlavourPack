using BigAndSmall;
using HarmonyLib;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack.Compat.SapientAnimals;

/// <summary>
/// Sapient mechanoids keep Filth_MachineBits from their source race. Swap them to normal
/// blood filth so hits leave blood instead of machine bits.
/// </summary>
[HarmonyPatch(typeof(HumanlikeAnimalGenerator), nameof(HumanlikeAnimalGenerator.GenerateHumanlikeAnimals))]
public static class Patch_SapientMechBlood
{
    private static readonly AccessTools.FieldRef<RaceProperties, ThingDef> BloodDef =
        AccessTools.FieldRefAccess<RaceProperties, ThingDef>("bloodDef");

    private static readonly AccessTools.FieldRef<RaceProperties, ThingDef> BloodSmearDef =
        AccessTools.FieldRefAccess<RaceProperties, ThingDef>("bloodSmearDef");

    public static void Postfix()
    {
        foreach (HumanlikeAnimal entry in HumanlikeAnimalGenerator.humanlikeAnimals.Values)
        {
            if (entry.animal?.race?.IsMechanoid != true)
                continue;

            RaceProperties race = entry.humanlikeAnimal?.race;
            if (race == null)
                continue;

            BloodDef(race) = ThingDefOf.Filth_Blood;
            BloodSmearDef(race) = ThingDefOf.Filth_BloodSmear;
        }
    }
}
