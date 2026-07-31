using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Shared lookup logic for the CFP_SkinnedAlive body/head texture swap. Not a patch itself.
///
/// PawnGraphicSet / ResolveAllGraphics do not exist in 1.6 - the only pipeline left is the
/// render tree (PawnRenderer -> PawnRenderTree -> PawnRenderNode), and PawnRenderNode_Body /
/// PawnRenderNode_Head are each `public override Graphic GraphicFor(Pawn)`, so postfixing those
/// two methods (see the three patch classes below) is a true swap: it inherits the correct mesh
/// set, layering, and portrait/atlas handling from the real node. A base PawnRenderNode.GraphicFor
/// patch would never fire, since both subclasses override it.
///
/// A declarative HediffDef.renderNodeProperties node (bodyTypeGraphicPaths) was rejected: it can
/// only ADD a node on top of the vanilla body, never replace it, because render-node skipFlags
/// are only ever raised by worn apparel (PawnRenderTree.AdjustParms) - a hediff can't raise one.
/// That would leave tinted vanilla skin peeking out around any soft edge in the art.
///
/// The peeled art is fully pre-coloured (raw-flesh red with a black outline), not a skin-shader
/// mask, so it is requested with ShaderDatabase.Cutout + Color.white rather than the pawn's own
/// skin shader/colour (which the vanilla Body node uses via colorType=Skin, useSkinShader=true).
/// </summary>
internal static class PeeledPawnGraphics
{
    private const string BodyTexPrefix = "Pawns/Peeled_";
    private const string HeadTexPrefix = "Pawns/PeeledHead_";

    // Paths confirmed absent via ContentFinder, so a failed lookup isn't repeated on every
    // graphics recache. GraphicDatabase already caches hits, so only misses need caching here.
    private static readonly HashSet<string> MissingTexPaths = new();

    public static bool ShouldReplace(Pawn pawn)
    {
        return pawn?.health?.hediffSet != null
            && pawn.health.hediffSet.HasHediff(CerebrexFlavourPackDefOf.CFP_SkinnedAlive)
            && !pawn.Drawer.renderer.StatueColor.HasValue
            && pawn.Drawer.renderer.CurRotDrawMode != RotDrawMode.Dessicated;
    }

    public static string BodyTexPathFor(Pawn pawn) => pawn.story?.bodyType == null
        ? null
        : BodyTexPrefix + pawn.story.bodyType.defName;

    // No PeeledHead_* art exists yet, so this always misses today and the vanilla head shows
    // through, tinted raw-flesh by CFP_SkinnedAlive's skinColorOverride. Drop matching art into
    // Common/Textures/Pawns/ later and it is picked up here with no code change.
    public static string HeadTexPathFor(Pawn pawn) => pawn.story?.headType == null
        ? null
        : HeadTexPrefix + pawn.story.headType.defName;

    public static Graphic TryGetReplacement(string path)
    {
        if (path == null || MissingTexPaths.Contains(path))
            return null;

        if (ContentFinder<Texture2D>.Get(path + "_south", reportFailure: false) == null)
        {
            MissingTexPaths.Add(path);
            return null;
        }

        return GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, Color.white);
    }
}

[HarmonyPatch(typeof(PawnRenderNode_Body), nameof(PawnRenderNode_Body.GraphicFor))]
public static class Harmony_PeeledPawn_Body
{
    public static void Postfix(Pawn pawn, ref Graphic __result)
    {
        if (!PeeledPawnGraphics.ShouldReplace(pawn))
            return;

        // Falls through to the vanilla (tinted) body when no art exists for this body type,
        // e.g. Child/Baby - "Peeled" art only covers Male/Female/Thin/Fat/Hulk today.
        Graphic replacement = PeeledPawnGraphics.TryGetReplacement(PeeledPawnGraphics.BodyTexPathFor(pawn));
        if (replacement != null)
            __result = replacement;
    }
}

[HarmonyPatch(typeof(PawnRenderNode_Head), nameof(PawnRenderNode_Head.GraphicFor))]
public static class Harmony_PeeledPawn_Head
{
    public static void Postfix(Pawn pawn, ref Graphic __result)
    {
        if (!PeeledPawnGraphics.ShouldReplace(pawn))
            return;

        Graphic replacement = PeeledPawnGraphics.TryGetReplacement(PeeledPawnGraphics.HeadTexPathFor(pawn));
        if (replacement != null)
            __result = replacement;
    }
}

/// <summary>
/// The body tattoo node is a child of Body at baseLayer 2, so left alone it would draw on top
/// of the replaced body texture. This is insurance, not a fix for a bug seen in practice - the
/// node already returns null without Ideology active or a body tattoo selected.
/// </summary>
[HarmonyPatch(typeof(PawnRenderNode_Tattoo_Body), nameof(PawnRenderNode_Tattoo_Body.GraphicFor))]
public static class Harmony_PeeledPawn_SuppressTattoo
{
    public static void Postfix(Pawn pawn, ref Graphic __result)
    {
        if (PeeledPawnGraphics.ShouldReplace(pawn))
            __result = null;
    }
}
