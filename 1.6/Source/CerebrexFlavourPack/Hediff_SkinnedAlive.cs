using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Permanent hediff applied by <see cref="CompPrisonerProcessor"/> when it ejects a survivor.
/// The def only sets <c>skinColorOverride</c> (no renderNodeProperties or skinShader), so
/// <c>HediffDef.HasDefinedGraphicProperties</c> is false and vanilla's AddHediff/RemoveHediff
/// paths (Pawn_HealthTracker) will NOT auto-refresh the pawn's cached body/head graphics.
/// The actual body/head texture swap is done by Harmony postfixes in Harmony_PeeledPawn.cs;
/// this override's only job is to force those postfixes to re-run by dirtying the render tree
/// on add/remove.
/// </summary>
public class Hediff_SkinnedAlive : HediffWithComps
{
    public override void PostAdd(DamageInfo? dinfo)
    {
        base.PostAdd(dinfo);
        pawn.Drawer.renderer.SetAllGraphicsDirty();
    }

    public override void PostRemoved()
    {
        base.PostRemoved();
        pawn.Drawer.renderer.SetAllGraphicsDirty();
    }
}
