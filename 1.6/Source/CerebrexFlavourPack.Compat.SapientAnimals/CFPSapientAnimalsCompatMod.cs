using HarmonyLib;
using Verse;

namespace CerebrexFlavourPack.Compat.SapientAnimals;

public class CFPSapientAnimalsCompatMod : Mod
{
    public CFPSapientAnimalsCompatMod(ModContentPack content) : base(content)
    {
        new Harmony("MrSamuelStreamer.rimworld.CerebrexFlavourPack.compat.sapientanimals")
            .PatchAll(typeof(CFPSapientAnimalsCompatMod).Assembly);
    }
}
