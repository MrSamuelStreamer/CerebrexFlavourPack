using HarmonyLib;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

public class CFPMechUpgradesCompatMod : Mod
{
    public CFPMechUpgradesCompatMod(ModContentPack content) : base(content)
    {
        new Harmony("MrSamuelStreamer.rimworld.CerebrexFlavourPack.compat.mechupgrades")
            .PatchAll(typeof(CFPMechUpgradesCompatMod).Assembly);
    }
}
