using System.Collections;
using RimWorld.Planet;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Global world draw layer that submits the black hole sphere mesh for rendering
/// once per frame while <see cref="GameCondition_BlackHole.IsActive"/> is true.
///
/// This class is instantiated by RimWorld from the
/// <c>CFP_BlackHoleDrawLayer</c> <see cref="GlobalWorldDrawLayerDef"/>,
/// and its <see cref="Render"/> method is called every frame from
/// <c>WorldRenderer.DrawWorldLayers()</c> — the same mechanism that drives
/// <c>GlobalDrawLayer_Sun</c>.
/// </summary>
public class WorldDrawLayer_BlackHole : WorldDrawLayerBase
{
    // Render on the WorldSkybox layer so WorldSkyboxCamera picks it up.
    protected override int RenderLayer => WorldCameraManager.WorldSkyboxLayer;

    private bool _loggedFirstCall;

    public override void Render()
    {
        // Diagnostic: confirm the layer was instantiated and Render() is being called.
        if (!_loggedFirstCall)
        {
            _loggedFirstCall = true;
            Log.Message($"[CerebrexFlavourPack DIAG] WorldDrawLayer_BlackHole.Render() called for the first time. " +
                        $"IsActive={GameCondition_BlackHole.IsActive}, " +
                        $"RenderLayer={RenderLayer}");
        }

        // Nothing to do while the game condition is inactive.
        if (!GameCondition_BlackHole.IsActive) return;

        // Delegate the actual Graphics.DrawMesh call to the condition class
        // which owns the mesh and material.
        GameCondition_BlackHole.SubmitWorldDrawCall(RenderLayer);
    }

    // We use Graphics.DrawMesh directly rather than the inherited subMesh system,
    // so Regenerate is a no-op.
    public override IEnumerable Regenerate()
    {
        dirty = false;
        yield break;
    }
}
