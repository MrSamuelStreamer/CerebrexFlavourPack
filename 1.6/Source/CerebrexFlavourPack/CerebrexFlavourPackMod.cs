using Verse;
using UnityEngine;
using HarmonyLib;

namespace CerebrexFlavourPack;

public class CerebrexFlavourPackMod : Mod
{
    public static Settings settings;

    public CerebrexFlavourPackMod(ModContentPack content) : base(content)
    {

        // initialize settings
        settings = GetSettings<Settings>();
#if DEBUG
        Harmony.DEBUG = true;
#endif
        Harmony harmony = new Harmony("MrSamuelStreamer.rimworld.CerebrexFlavourPack.main");	
        harmony.PatchAll();
    }

    public override void DoSettingsWindowContents(Rect inRect)
    {
        base.DoSettingsWindowContents(inRect);
        settings.DoWindowContents(inRect);
    }

    public override string SettingsCategory()
    {
        return "CerebrexFlavourPack_SettingsCategory".Translate();
    }
}
