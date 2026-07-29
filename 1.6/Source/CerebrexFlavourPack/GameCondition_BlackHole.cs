using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Game condition: the home system's star has been replaced by a black hole.
///
/// Two effects:
///   1. In-map: caps sunlight at 25 % via SkyTarget.
///   2. World-map: replaces the vanilla sun disc with a ray-marched black hole
///      rendered by <c>BlackHoleRayMarch.shader</c>.
///
/// Rendering approach (ported from SkipTech's "screen-space GL" design):
///   • A Harmony patch (<see cref="Harmony_BlackHoleSun_HideSun"/>) suppresses
///     the vanilla <see cref="GlobalDrawLayer_Sun"/> draw call while active.
///   • A <see cref="BHRenderHelper"/> MonoBehaviour is attached to the
///     WorldSkyboxCamera's GameObject.  Its <c>OnPostRender()</c> callback fires
///     every frame the camera renders.
///   • We draw a GL disc (GL.Begin + GL.TRIANGLES, GL.LoadOrtho) sized to the
///     sphere's angular extent on screen, then the fragment shader reconstructs
///     world-space rays from bilinear-interpolated frustum far-plane corners.
///
/// Why this approach works on Unity 2022 / Metal (Apple Silicon):
///   • Graphics.DrawMesh / DrawMeshNow: Metal GPU encoder is closed before
///     OnPostRender fires → silently produces no pixels.
///   • GL.LoadProjectionMatrix + GL.MultMatrix: do NOT update UNITY_MATRIX_VP /
///     unity_ObjectToWorld in Unity 2022, so UnityObjectToClipPos misbehaves.
///   • GL.LoadOrtho + GL.Vertex3(viewport_UV): confirmed working on all platforms.
///   • Ray reconstruction via cam.ViewportToWorldPoint frustum corners avoids
///     platform-specific GPU projection matrix convention differences.
///
/// Unlike the source this was ported from, this condition is not player-triggered
/// by a building — it is registered permanently (or removed) by
/// <see cref="WorldComponent_CerebrexBlackHole"/> based on a mod setting.
/// </summary>
[StaticConstructorOnStartup]
public class GameCondition_BlackHole : GameCondition
{
    // ── Sky colours ──────────────────────────────────────────────────────────

    // SkyManager blends a condition's SkyTarget in via LerpDarken, which takes the
    // per-channel Min() of the natural sky colour and this one, then lerps toward
    // that Min by the (now fully-ramped, weight-1) SkyTargetLerpFactor. That means
    // whatever colour we return at cap=1 becomes the *entire* visible tint once
    // ramped in — a fixed dim-blue "max" (as this used to return) reads as
    // permanent moody nighttime even while the glow float correctly reports
    // "brightly lit", because glow and colour are two independent SkyTarget fields.
    //
    // Anchoring cap=1 at white fixes that: Min(natural, white) == natural, so a
    // 100% light cap is a genuine no-op on colour (matches an ordinary star) and
    // only the near-black flavour tint at the low end actually darkens anything —
    // consistent with "cap = fraction of normal sunlight let through".
    private static readonly Color SkyNearBlack     = new ColorInt(5, 5, 12).ToColor;
    private static readonly Color ShadowNearBlack  = new ColorInt(3, 3,  8).ToColor;
    private static readonly Color OverlayNearBlack = new ColorInt(2, 2,  6).ToColor;

    private static SkyColorSet ComputeColors(float cap)
    {
        return new SkyColorSet(
            Color.Lerp(SkyNearBlack, Color.white, cap),
            Color.Lerp(ShadowNearBlack, Color.white, cap),
            Color.Lerp(OverlayNearBlack, Color.white, cap),
            Mathf.Lerp(0.1f, 1f, cap)   // sky-gas saturation — desaturated at low caps, untouched at cap=1
        );
    }

    // ── Static assets (loaded once at startup) ────────────────────────────────

    private static readonly Shader    RayMarchShader;
    private static readonly Texture2D DiscTexture;

    static GameCondition_BlackHole()
    {
        RayMarchShader = ContentFinder<Shader>.Get("BlackHoleRayMarch", reportFailure: true);

        if (RayMarchShader != null)
            ModLog.Log($"[DIAG] BlackHoleRayMarch shader loaded OK. " +
                       $"isSupported={RayMarchShader.isSupported}");
        else
            ModLog.Warn(
                "[DIAG] BlackHoleRayMarch shader NOT FOUND in mod asset bundles. " +
                "Rebuild (CerebrexFlavourPack/Label All Assets + Build Bundles) and confirm " +
                "Assets/Data is included in the label pass.");

        DiscTexture = ContentFinder<Texture2D>.Get("AccretionDisc", reportFailure: false);
        ModLog.Log($"[DIAG] AccretionDisc texture: " +
                   $"{(DiscTexture != null ? "loaded" : "not found (OK — shader uses white fallback)")}.");
    }

    // ── Runtime state ─────────────────────────────────────────────────────────

    // Static so Harmony_BlackHoleSun and WorldDrawLayer_BlackHole can read without
    // holding a reference to the GameCondition instance.
    private static Material       bhMaterial;
    private static BHRenderHelper bhRenderHelper;

    /// <summary>True while the black hole is active and the render helper is attached.</summary>
    internal static bool IsActive => bhRenderHelper != null;

    /// <summary>
    /// Fraction of normal sunlight emitted by the black hole (world-map planet lighting).
    /// 0.5 = 50 % — enough for playability while clearly dimmer than a real star.
    /// </summary>
    internal const float WorldLightFactor = 0.5f;

    // ── Growth ─────────────────────────────────────────────────────────────

    // Tick this black hole began, captured on activation. Comes from the GameCondition's
    // own (already-scribed) startTick, so growth needs no new save data of its own.
    private static int bhStartTick;

    /// <summary>
    /// Radius multiplier applied to every black hole dimension, derived from the
    /// condition's age. <see cref="Settings.blackHoleGrowthRate"/> is in *area* doublings
    /// per in-game year, and doubling an area means multiplying the radius by sqrt(2) —
    /// hence the 0.5 exponent. Clamped to <see cref="Settings.blackHoleGrowthMax"/> so a
    /// long-lived colony never ends up with an all-black world map.
    /// </summary>
    internal static float GrowthFactor
    {
        get
        {
            Settings s = CerebrexFlavourPackMod.settings;
            if (!s.blackHoleGrowthEnabled || Find.TickManager == null) return 1f;
            float years = (Find.TickManager.TicksGame - bhStartTick) / (float)GenDate.TicksPerYear;
            if (years <= 0f) return 1f;
            return Mathf.Min(Mathf.Pow(2f, 0.5f * years * s.blackHoleGrowthRate), s.blackHoleGrowthMax);
        }
    }

    // ── SkyTarget ────────────────────────────────────────────────────────────

    public override SkyTarget? SkyTarget(Map map)
    {
        float cap = CerebrexFlavourPackMod.settings.blackHoleLightCap;
        return new SkyTarget(cap, ComputeColors(cap), 0.5f, 0.3f);
    }

    // lerpTarget = 1f (the default) so the condition's SkyTarget fully overrides the
    // vanilla sky once ramped in — otherwise LerpInOutValue's blend weight caps below
    // 1, and the map's actual glow ends up as a mix of vanilla daylight and our target
    // instead of matching blackHoleLightCap (e.g. capping the blend at 0.5 makes a 25%
    // light-cap setting read as ~62% on the map: halfway between vanilla ~100% and 25%).
    public override float SkyTargetLerpFactor(Map map) =>
        GameConditionUtility.LerpInOutValue(this, 5000f);

    public override bool AllowEnjoyableOutsideNow(Map map) => false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void Init()
    {
        base.Init();
        ActivateBlackHole(startTick);
    }

    public override void End()
    {
        base.End();
        DeactivateBlackHole();
    }

    // ── Sphere management ─────────────────────────────────────────────────────

    // World-space sphere radius.  Must match _SphereRadius in the shader.
    // The vanilla sun quad is 15 world units wide at sunDir*20f.  From the skybox
    // camera (~1400 wu away) this subtends ~0.3° — invisible behind the planet.
    // We use a much larger radius so the accretion disc rim extends into the visible
    // sky around the planet globe.  150 wu ≈ 6° angular radius at default zoom,
    // larger than the planet disc (~4°), so the glowing disc halo is always visible.
    internal const float SphereWorldRadius = 150f;

    internal static void ActivateBlackHole(int startTick)
    {
        bhStartTick = startTick;

        if (RayMarchShader == null)
        {
            ModLog.Warn("GameCondition_BlackHole: shader not loaded — black hole inactive.");
            return;
        }
        // Clean up any stale render helper left over from a previous game session.
        // Happens when a game with an active BH is abandoned (new game / load) without
        // End() being called on the condition — because RimWorld only calls End() when
        // a condition expires normally, not on game tear-down.
        if (bhRenderHelper != null)
        {
            ModLog.Warn("GameCondition_BlackHole.ActivateBlackHole: found stale render helper — cleaning up before re-activation.");
            DeactivateBlackHole();
        }

        // ── Material ──────────────────────────────────────────────────────
        // 2D billboard approach: uniforms are in viewport-space, set per-frame
        // in OnPostRender.  Only fixed knobs are set here.
        bhMaterial = new Material(RayMarchShader);
        bhMaterial.SetFloat("_DiscSpeed", 3.5f);
        if (DiscTexture != null)
            bhMaterial.SetTexture("_DiscTex", DiscTexture);

        // ── Attach render helper to WorldSkyboxCamera ─────────────────────
        // OnPostRender() fires every frame the camera renders — guaranteed on
        // Unity BRP Forward cameras regardless of clearFlags or skybox setup.
        Camera skyboxCam   = WorldCameraManager.WorldSkyboxCamera;
        bhRenderHelper     = skyboxCam.gameObject.AddComponent<BHRenderHelper>();
        bhRenderHelper.Mat = bhMaterial;

        ModLog.Log($"[DIAG] Black hole ACTIVATED — shader={RayMarchShader.name}, " +
                   $"BHRenderHelper attached to GO={skyboxCam.gameObject.name}.");
    }

    internal static void DeactivateBlackHole()
    {
        if (bhRenderHelper != null)
        {
            Object.Destroy(bhRenderHelper);
            bhRenderHelper = null;
        }
        if (bhMaterial != null)
        {
            Object.Destroy(bhMaterial);
            bhMaterial = null;
        }
        ModLog.Log("Black hole deactivated.");
    }

    // ── Called by WorldDrawLayer_BlackHole each frame ─────────────────────────
    // Actual rendering is handled by BHRenderHelper.OnPostRender.
    // This method is kept for the diagnostic draw-call counter and IsActive guard.

    private static int _drawCallCount;

    internal static void SubmitWorldDrawCall(int renderLayer)
    {
        if (!IsActive) return;
        _drawCallCount++;
        if (_drawCallCount == 1 || _drawCallCount % 300 == 0)
            ModLog.Log($"[DIAG] SubmitWorldDrawCall #{_drawCallCount} — " +
                       $"sunDir={GenCelestial.CurSunPositionInWorldSpace().normalized:F2} " +
                       $"(rendering via BHRenderHelper.OnPostRender)");
    }
}

// ── Render helper ─────────────────────────────────────────────────────────────

/// <summary>
/// MonoBehaviour attached to the WorldSkyboxCamera's GameObject.
///
/// Each frame:
///   1. Computes the black hole's viewport position and angular screen radius.
///   2. Uploads per-frame camera uniforms to the material:
///      – Frustum far-plane world-space corners for ray reconstruction.
///      – BH world position, radius, and disc normal.
///   3. Draws a GL disc in viewport UV space covering the BH's projected area.
///
/// The fragment shader reconstructs world-space rays from the screen UV + frustum
/// corners, then ray-marches the Schwarzschild metric to render the event horizon
/// and accretion disc.
///
/// Root cause of all previous rendering failures on Unity 2022 / Metal:
///   Metal closes the GPU render encoder before OnPostRender fires.  Any draw path
///   that goes through the Metal encoder (DrawMesh, CommandBuffer, DrawMeshNow)
///   silently produces no pixels.  GL.Begin/End uses a separate immediate-mode
///   flush path that still works at this stage.
/// </summary>
internal sealed class BHRenderHelper : MonoBehaviour
{
    internal Material Mat;

    // Overall disc size relative to the vanilla sun viewport formula, at year 0 (before
    // any growth is applied). Increase toward 0.40 for a more prominent disc; decrease
    // toward 0.15 for subtler. At 0.25: disc outer ≈ the planet's angular diameter —
    // visible but genuinely distant. Internal so GlobalDrawLayer_Stars_Spectral can derive
    // its lensing angles from the same base instead of duplicating it.
    internal const float BHDiscScaleBase = 0.25f;

    private int _frameCount;

    private void OnPostRender()
    {
        if (Mat == null) return;

        _frameCount++;
        Camera cam = Camera.current;
        if (cam == null) return;

        // Sun's projected viewport position — disc is always centred here.
        // Project from the camera's own position in the sun direction; this gives
        // the correct sky position regardless of where the camera is in world space.
        // (sunDir * 20f would project to near screen-centre because that world point
        //  is negligibly close to the planet origin from the camera's distance.)
        Vector3 sunDir   = GenCelestial.CurSunPositionInWorldSpace().normalized;
        Vector3 vp       = cam.WorldToViewportPoint(cam.transform.position + sunDir * 1000f);
        if (vp.z <= 0f) return;

        float aspect = (float)Screen.width / Screen.height;

        // ── Viewport-space disc geometry ───────────────────────────────────
        // The vanilla sun quad (half-extent 7.5 wu) is placed at sunDir*20f from
        // the world origin and rendered by WorldSkyboxCamera (FOV 60°, fixed).
        // The camera orbits at altitude 225–1200 wu from origin — always ≫ 20 wu.
        // Because camDist >> 20, the sun-to-camera distance barely changes with zoom,
        // so the sun's apparent angular size is essentially constant.  The user
        // perceives the sun as a fixed-size sky object.
        //
        // To match this behaviour for the BH, use the sun's fixed world-position
        // distance (20 wu from origin ≈ "at camera" for sky objects) as the reference
        // distance instead of the variable camDist.  This makes sunVpRadius
        // camera-distance-independent: the BH appears a constant size at all zoom levels.
        //
        //   sunVpRadius = 7.5 / (2 * 20 * tan(30°)) ≈ 0.325  — constant
        //
        // (Using camDist in the denominator caused the BH to scale 5× across the
        // zoom range, which is visually obvious because the disc is large on screen.)
        float fovHalfTan    = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float sunVpRadius   = 7.5f / (2f * 20f * fovHalfTan);  // ≈ 0.325, zoom-invariant

        // Perspective correction: a fixed viewport radius represents a smaller angular
        // extent off-axis (sec²(α) effect).  Scale all radii by 1/cos²(α) so the disc
        // maintains a constant apparent angular size at all screen positions, matching
        // the vanilla sun (a world-space mesh that gets this correction automatically).
        //   vp.z = dot(sunDir × 1000, cam.forward) = 1000 × cos(α)
        //   max on-screen: α = 30° (FOV edge) → sec²(30°) ≈ 1.33 — mild, continuous.
        float cosAlpha      = Mathf.Max(vp.z / 1000f, 0.5f);   // clamp guards /0; not active in FOV
        float rawPerspScale = 1f / (cosAlpha * cosAlpha);

        // The ratio above is constant regardless of disc size, so it reads as a few
        // invisible pixels at the small base scale but a jarring "looming closer near
        // screen edges" swing once GrowthFactor makes the disc screen-filling. Dampen the
        // correction toward a no-op (1x) as the disc grows, so it stays exactly the
        // original behaviour at year 0 (dampen = 1) while a fully-grown disc holds a
        // near-constant apparent size across the screen instead of ballooning at the edges.
        float dampen     = 1f / GameCondition_BlackHole.GrowthFactor;
        float perspScale = Mathf.Lerp(1f, rawPerspScale, dampen);

        // BHDiscScaleBase reduces the overall disc to ~1.5× the planet's angular radius at
        // year 0 so the BH reads as a distant stellar object rather than something nearby.
        // GrowthFactor scales that up over time (1x at year 0, capped at blackHoleGrowthMax).
        // perspScale corrects for perspective distortion off screen centre.
        float discScale     = BHDiscScaleBase * GameCondition_BlackHole.GrowthFactor;
        float scaledRadius  = sunVpRadius * discScale * perspScale;
        float horizonRadius = scaledRadius * 0.22f;   // ≈ 0.025 at centre, at base scale
        float discOuter     = scaledRadius * 0.77f;   // ≈ 0.088 at centre, at base scale

        // ── Disc orientation ─────────────────────────────────────────────
        // The disc lies in the stellar equatorial plane (normal ≈ worldUp projected
        // perpendicular to sunDir).  As the camera orbits the planet, the disc
        // mid-plane appears at a different screen angle; we project a disc-right
        // vector into viewport space and pass the angle to the shader each frame.
        Vector3 rawUp    = Vector3.up;
        float   upDotSun = Vector3.Dot(rawUp, sunDir);
        Vector3 discAxis = rawUp - upDotSun * sunDir; // disc rotation axis (world space)
        if (discAxis.sqrMagnitude < 0.01f)            // sun near pole — use forward
        {
            rawUp    = Vector3.forward;
            upDotSun = Vector3.Dot(rawUp, sunDir);
            discAxis = rawUp - upDotSun * sunDir;
        }
        discAxis = discAxis.normalized;

        // A vector lying in the disc mid-plane, perpendicular to sunDir.
        // Projecting this into viewport space gives the disc's screen orientation.
        Vector3 discRight  = Vector3.Cross(sunDir, discAxis).normalized;
        Vector3 vpDiscRight = cam.WorldToViewportPoint(
                                  cam.transform.position + sunDir * 1000f + discRight * 100f);
        float   discAngle   = Mathf.Atan2(vpDiscRight.y - vp.y,
                                          (vpDiscRight.x - vp.x) * aspect);

        Mat.SetVector("_SunVP",           new Vector4(vp.x, vp.y, 0f, 0f));
        Mat.SetFloat ("_HorizonRadius",   horizonRadius);
        Mat.SetFloat ("_DiscOuterRadius", discOuter);
        Mat.SetFloat ("_Aspect",          aspect);
        Mat.SetFloat ("_DiscAngle",       discAngle);

        if (_frameCount == 1)
            ModLog.Log($"[BH] First billboard render: sunVP={vp:F3}, aspect={aspect:F2}");

        // GL disc slightly larger than the visual disc to avoid edge clipping.
        // Padding scaled by perspScale so it stays proportional at all screen positions.
        float glRadiusV = discOuter + 0.03f * perspScale;
        float glRadiusU = glRadiusV / aspect;

        Mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadOrtho();
        GL.Begin(GL.TRIANGLES);
        DrawDisc(vp.x, vp.y, glRadiusU, glRadiusV);
        GL.End();
        GL.PopMatrix();
    }

    // Must be called between GL.Begin(GL.TRIANGLES) and GL.End().
    private static void DrawDisc(float cx, float cy, float rx, float ry, int segs = 48)
    {
        for (int i = 0; i < segs; i++)
        {
            float a0 = (float)i       / segs * 2f * Mathf.PI;
            float a1 = (float)(i + 1) / segs * 2f * Mathf.PI;
            GL.Vertex3(cx,                       cy,                       0f);
            GL.Vertex3(cx + Mathf.Cos(a0) * rx,  cy + Mathf.Sin(a0) * ry,  0f);
            GL.Vertex3(cx + Mathf.Cos(a1) * rx,  cy + Mathf.Sin(a1) * ry,  0f);
        }
    }
}
