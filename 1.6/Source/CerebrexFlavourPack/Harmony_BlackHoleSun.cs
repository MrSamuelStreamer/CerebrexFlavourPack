using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Suppresses the vanilla sun quad while the black hole condition is active.
///
/// We patch <see cref="WorldDrawLayerBase.Render"/> — the base-class method that
/// calls <c>Graphics.DrawMesh</c> for every layer sub-mesh.  When the instance is
/// <see cref="GlobalDrawLayer_Sun"/> and the black hole sphere is live, we skip the
/// draw entirely so the 3-D sphere takes over the sun's visual role.
///
/// Using the base-class method rather than a property means one patch covers the
/// full render path.  The <c>is GlobalDrawLayer_Sun</c> guard keeps it surgical —
/// all other world layers render normally.
/// </summary>
[HarmonyPatch(typeof(WorldDrawLayerBase), "Render")]
public static class Harmony_BlackHoleSun_HideSun
{
    public static bool Prefix(WorldDrawLayerBase __instance)
    {
        if (__instance is GlobalDrawLayer_Sun && GameCondition_BlackHole.IsActive)
            return false;   // skip vanilla sun draw

        return true;        // all other layers render normally
    }
}

/// <summary>
/// Cleans up any stale black-hole render helper before a save is loaded into an
/// already-running game session (File > Load without quitting to desktop first).
///
/// RimWorld never calls <see cref="GameCondition.End"/> when the current game is
/// abandoned — it simply discards the old <see cref="Game"/> object.  Because
/// <see cref="BHRenderHelper"/> is a MonoBehaviour attached to the persistent
/// <c>WorldSkyboxCamera</c> (which is <c>DontDestroyOnLoad</c>), it would survive
/// into the new game session and continue rendering the black hole even before
/// the loaded save's own condition state has been restored.
///
/// There is deliberately no equivalent patch on <c>Game.InitNewGame</c>: world
/// generation (and therefore <see cref="WorldComponent_CerebrexBlackHole.FinalizeInit"/>,
/// which re-activates the condition) runs *before* <c>InitNewGame</c> is called, so a
/// Prefix there would tear down a black hole that was just correctly activated for the
/// new world. <see cref="GameCondition_BlackHole.ActivateBlackHole"/> already
/// self-heals any stale helper the moment it runs again, which covers the new-game
/// case without needing a Harmony hook.
/// </summary>
[HarmonyPatch(typeof(Game), "LoadGame")]
public static class Harmony_Game_LoadGame_CleanupBlackHole
{
    public static void Prefix() => GameCondition_BlackHole.DeactivateBlackHole();
}

/// <summary>
/// Dims the planet globe in world-map view while the black hole is active.
///
/// <see cref="WorldRendererUtility.UpdateGlobalShadersParams"/> sets the global
/// <c>_PlanetSunLightDirection</c> vector every frame.  All planet surface shaders
/// compute the lit factor as <c>saturate(-dot(surfaceNormal, lightDir))</c>, so
/// scaling the vector magnitude by <see cref="GameCondition_BlackHole.WorldLightFactor"/>
/// scales the maximum brightness by the same factor — 50 % by default.
///
/// The terminator position and night-side colour are unaffected: the direction is
/// unchanged, only the magnitude shrinks.
/// </summary>
[HarmonyPatch(typeof(WorldRendererUtility), nameof(WorldRendererUtility.UpdateGlobalShadersParams))]
public static class Harmony_WorldLight_DimPlanet
{
    public static void Postfix()
    {
        if (!GameCondition_BlackHole.IsActive) return;

        // Read back the value just set by vanilla, scale it, and re-set it.
        // Using GetGlobalVector avoids duplicating the GenCelestial call.
        Vector3 dir = Shader.GetGlobalVector(ShaderPropertyIDs.PlanetSunLightDirection);
        Shader.SetGlobalVector(ShaderPropertyIDs.PlanetSunLightDirection,
                               dir * GameCondition_BlackHole.WorldLightFactor);
    }
}
