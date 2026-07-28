using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Keeps the <see cref="GameCondition_BlackHole"/> world condition in sync with
/// <see cref="Settings.blackHoleSunEnabled"/>.
///
/// Unlike the building this was ported from (which activated the condition for a
/// fixed 30-day window via a player-built gizmo), here the black hole is a
/// permanent trait of the home system, controlled purely by the mod setting:
/// on by default, and toggleable at any time — including mid-game, via
/// <see cref="Settings.DoWindowContents"/> calling <see cref="SyncActiveStateWithSetting"/>
/// directly.
///
/// Mirrors vanilla's own pattern for a permanent condition
/// (<see cref="ScenPart_PermaGameCondition.GenerateIntoMap"/>): build via
/// <see cref="GameConditionMaker.MakeConditionPermanent"/> and register on the
/// world's <see cref="GameConditionManager"/> — no duration, no expiry.
/// </summary>
public class WorldComponent_CerebrexBlackHole : WorldComponent
{
    public WorldComponent_CerebrexBlackHole(World world) : base(world)
    {
    }

    public override void FinalizeInit(bool fromLoad)
    {
        base.FinalizeInit(fromLoad);
        SyncActiveStateWithSetting();
    }

    /// <summary>
    /// Registers or ends the permanent black hole condition on the current world so it
    /// matches <see cref="Settings.blackHoleSunEnabled"/>. Safe to call with no world
    /// loaded (e.g. from the mod settings screen at the main menu) — it's a no-op then.
    /// </summary>
    internal static void SyncActiveStateWithSetting()
    {
        World world = Find.World;
        if (world?.GameConditionManager == null) return;

        bool shouldBeActive = CerebrexFlavourPackMod.settings.blackHoleSunEnabled;
        GameCondition active = world.GameConditionManager.GetActiveCondition(CerebrexFlavourPackDefOf.CFP_BlackHole);

        if (shouldBeActive && active == null)
        {
            GameCondition condition = GameConditionMaker.MakeConditionPermanent(CerebrexFlavourPackDefOf.CFP_BlackHole);
            world.GameConditionManager.RegisterCondition(condition);
            ModLog.Log("[WorldComponent_CerebrexBlackHole] Black hole enabled (permanent, via setting).");
        }
        else if (shouldBeActive && active != null && !GameCondition_BlackHole.IsActive)
        {
            GameCondition_BlackHole.ActivateBlackHole();
            ModLog.Log("[WorldComponent_CerebrexBlackHole] Re-attached render helper for save-loaded black hole condition.");
        }
        else if (!shouldBeActive && active != null)
        {
            active.End();
            ModLog.Log("[WorldComponent_CerebrexBlackHole] Black hole disabled via setting.");
        }
    }
}
