using HarmonyLib;
using Verse;

namespace CerebrexFlavourPack.Compat.Hospitality;

/// <summary>
/// Entry point for the Hospitality + Sapient Animals compat DLL.
///
/// The DLL is loaded only when both Orion.Hospitality and
/// RedMattis.SapientAnimals are active. Gating is done in loadFolders.xml
/// on the Compatibility/Orion.Hospitality tree, which is also where this
/// project's build output is dropped.
///
/// This class only exists so Harmony picks up the assembly. The actual
/// work happens in <see cref="GameComponent_CerebrexEmissaryRetrofit"/>,
/// a plain GameComponent that RimWorld constructs during save/load.
/// </summary>
public class CFPHospitalityCompatMod : Mod
{
    public CFPHospitalityCompatMod(ModContentPack content) : base(content)
    {
        new Harmony("MrSamuelStreamer.rimworld.CerebrexFlavourPack.compat.hospitality")
            .PatchAll(typeof(CFPHospitalityCompatMod).Assembly);
    }
}
