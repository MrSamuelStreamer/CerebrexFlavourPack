using System.Collections;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Drop-in replacement for <see cref="GlobalDrawLayer_Stars"/> that renders
/// each star with a colour drawn from a realistic stellar spectral-type
/// distribution instead of using the single flat-white vanilla material.
///
/// Registered via a PatchOperation that replaces
/// <c>GlobalWorldDrawLayerDef[Stars].worldDrawLayer</c> — no Harmony required.
///
/// Spectral-type colour mapping (approximate Planck / empirical):
///   O  blue-violet    0.1 % of stars
///   B  blue-white     0.4 %
///   A  white          1.5 %
///   F  yellow-white   5.0 %
///   G  yellow        12.0 %
///   K  orange        24.0 %
///   M  red-orange    57.0 %  (most common — cool M dwarfs dominate)
/// </summary>
[StaticConstructorOnStartup]
public class GlobalDrawLayer_Stars_Spectral : WorldDrawLayerBase
{
    // ── Reproduced from vanilla (all private in GlobalDrawLayer_Stars) ───
    private bool         calculatedForStaticRotation;
    private PlanetTile   calculatedForStartingTile = PlanetTile.Invalid;
    // Tracks the IsActive state at last Regenerate so ShouldRegenerate detects transitions.
    private bool         calculatedForBlackHoleActive = false;

    private const float            DistanceToStars = 20f;
    private static readonly FloatRange StarsDrawSize = new FloatRange(1f, 3.8f);

    // Higher count than vanilla (1500) to fill Poisson-distribution voids
    // and compensate for the extra spectral-type Rand.Value draw per star
    // shifting position seeds.
    private const int StarsCount = 2500;

    protected override int       RenderLayer => WorldCameraManager.WorldSkyboxLayer;
    private           bool       UseStaticRotation => Current.ProgramState == ProgramState.Entry;

    protected override Quaternion Rotation
    {
        get
        {
            if (UseStaticRotation)
                return Quaternion.identity;
            return Quaternion.LookRotation(GenCelestial.CurSunPositionInWorldSpace());
        }
    }

    public override bool ShouldRegenerate
    {
        get
        {
            // Regenerate whenever the black hole activates or deactivates so the
            // sun-proximity shrinkage can be toggled without a manual dirty flag.
            if (GameCondition_BlackHole.IsActive != calculatedForBlackHoleActive)
                return true;
            if (!base.ShouldRegenerate &&
                (Find.GameInitData == null ||
                 !(Find.GameInitData.startingTile != calculatedForStartingTile)))
                return UseStaticRotation != calculatedForStaticRotation;
            return true;
        }
    }

    // ── Spectral-type material cache ─────────────────────────────────────
    // Built once on first Regenerate() call so WorldMaterials.Stars is
    // guaranteed to be loaded before we copy it.

    private static Material[] spectralMats;

    // Spectral type colours and their CUMULATIVE probability weights.
    // Weights are roughly IMF-adjusted so cool M/K dwarfs dominate the draw.
    private static readonly (Color col, float cdf)[] SpectralCDF =
    {
        (new Color(0.64f, 0.74f, 1.00f), 0.001f),  // O — blue-violet
        (new Color(0.72f, 0.84f, 1.00f), 0.005f),  // B — blue-white
        (new Color(0.93f, 0.95f, 1.00f), 0.020f),  // A — white
        (new Color(1.00f, 0.98f, 0.84f), 0.070f),  // F — yellow-white
        (new Color(1.00f, 0.94f, 0.68f), 0.190f),  // G — yellow  (Sun-like)
        (new Color(1.00f, 0.76f, 0.42f), 0.430f),  // K — orange
        (new Color(1.00f, 0.54f, 0.22f), 1.000f),  // M — red-orange
    };

    // Static constructor — satisfies RimWorld's [StaticConstructorOnStartup]
    // requirement for types that hold UnityEngine asset references, and
    // ensures the materials are created on the main thread at startup rather
    // than lazily on first Regenerate() call.
    static GlobalDrawLayer_Stars_Spectral()
    {
        GetOrBuildSpectralMats();
    }

    private static Material[] GetOrBuildSpectralMats()
    {
        if (spectralMats != null) return spectralMats;

        spectralMats = new Material[SpectralCDF.Length];
        for (int i = 0; i < SpectralCDF.Length; i++)
        {
            // Copy the vanilla Stars material so we inherit its shader and
            // texture, then tint it with the spectral colour.
            var mat = new Material(WorldMaterials.Stars);
            mat.color = SpectralCDF[i].col;
            spectralMats[i] = mat;
        }
        return spectralMats;
    }

    private static int PickSpectralIndex(float t)
    {
        for (int i = 0; i < SpectralCDF.Length; i++)
            if (t <= SpectralCDF[i].cdf) return i;
        return SpectralCDF.Length - 1;
    }

    // ── Gravitational lensing constants ──────────────────────────────────
    // All angles are in radians, measured in mesh-local space where Vector3.forward
    // points toward the black hole (i.e., the sun direction after mesh rotation).
    //
    // These constants track BHDiscScale (= 0.25) in BHRenderHelper:
    //   Horizon viewport radius: sunVpRadius × 0.22 × 0.25 = 0.018
    //   tan(θ_h) = 0.018 × 2 × tan(30°) = 0.018 × 1.155 ≈ 0.021 rad ≈ 1.2°
    //
    // LensStrength is unchanged; the 1/θ formula still produces an Einstein ring
    // just outside the new (smaller) disc outer edge.
    private const float LensHorizonTheta = 0.021f;  // mesh-space angle ≈ event horizon (tracks BHDiscScale)
    private const float LensShadowTheta  = 0.018f;  // skip stars fully behind the shadow
    private const float LensRadius       = 0.20f;   // ~11.5° outer radius of lensing zone
    private const float LensStrength     = 2.8f;    // bending multiplier (tunable)

    // ── Regenerate ───────────────────────────────────────────────────────

    public override IEnumerable Regenerate()
    {
        // base.Regenerate() clears subMeshes and resets dirty flag.
        foreach (object o in base.Regenerate()) yield return o;

        var mats = GetOrBuildSpectralMats();

        Rand.PushState();
        Rand.Seed = Find.World.info.Seed;

        for (int i = 0; i < StarsCount; i++)
        {
            // ── Consume ALL random draws up-front so the Rand seed advances
            //    identically regardless of whether this star is skipped later.
            Vector3 unitVector = Rand.UnitVector3;
            int     specIdx    = PickSpectralIndex(Rand.Value);
            float   size       = StarsDrawSize.RandomInRange;
            float   quadAngle  = Rand.Range(0f, 360f);

            // Vanilla sun-proximity shrinkage: fade stars near the sun direction to
            // simulate glare washing them out.  When the black hole is active its
            // dark event horizon sits in that region — shrinking stars there would
            // produce a distracting empty halo, so we skip it only for that case.
            if (!GameCondition_BlackHole.IsActive)
            {
                // In gameplay mode the layer is rotated so Vector3.forward is the
                // sun direction in local/mesh space (mirrors vanilla exactly).
                Vector3 sunRef = UseStaticRotation
                    ? GenCelestial.CurSunPositionInWorldSpace().normalized
                    : Vector3.forward;
                float sunDot = Vector3.Dot(unitVector, sunRef);
                if (sunDot > 0.8f)
                    size *= GenMath.LerpDouble(0.8f, 1f, 1f, 0.35f, sunDot);
            }

            // ── Gravitational lensing (gameplay only, BH active) ─────────────
            // Stars are deflected radially away from the BH direction using a
            // simplified 1/θ Schwarzschild bending formula.  Stars from a wide
            // range of source angles converge to roughly the same apparent angle
            // just outside the accretion disc, producing a visible Einstein ring.
            if (GameCondition_BlackHole.IsActive && !UseStaticRotation)
            {
                // In gameplay mode the mesh is rotated so the BH is always at
                // Vector3.forward in mesh-local space.
                float dot   = unitVector.z;                           // = Dot(unitVector, forward)
                float sinTh = Mathf.Sqrt(Mathf.Max(0f, 1f - dot * dot));
                float theta = Mathf.Atan2(sinTh, dot);               // angular separation [0, π]

                if (theta < LensShadowTheta)
                    continue; // fully behind the event horizon — BH shader draws black here

                if (theta < LensRadius && sinTh > 1e-4f)
                {
                    // 1/θ bending, smoothly fading to zero at LensRadius.
                    float raw   = LensStrength * (LensHorizonTheta * LensHorizonTheta) / theta;
                    float fade  = Mathf.SmoothStep(LensRadius, LensRadius * 0.25f, theta);
                    float delta = Mathf.Min(raw * fade, Mathf.PI * 0.3f);

                    // New position: decompose unitVector into (forward, perp) basis and
                    // rotate by delta away from the BH — exact, no small-angle approximation.
                    float   thetaN = theta + delta;
                    Vector3 perp   = new Vector3(unitVector.x, unitVector.y, 0f) / sinTh;
                    unitVector     = Mathf.Cos(thetaN) * Vector3.forward
                                   + Mathf.Sin(thetaN) * perp;

                    // Lensing magnification: stars deflected from near the horizon
                    // appear larger, brightening the Einstein ring band.
                    float magFrac = Mathf.Clamp01(1f - theta / (LensHorizonTheta * 4f));
                    size *= 1f + magFrac * 1.5f;
                }
            }

            Vector3      pos     = unitVector * DistanceToStars;
            LayerSubMesh subMesh = GetSubMesh(mats[specIdx]);
            WorldRendererUtility.PrintQuadTangentialToPlanet(
                pos, size, 0f, subMesh, counterClockwise: true, quadAngle);
        }

        calculatedForStartingTile   = Find.GameInitData?.startingTile ?? PlanetTile.Invalid;
        calculatedForStaticRotation = UseStaticRotation;
        calculatedForBlackHoleActive = GameCondition_BlackHole.IsActive;

        Rand.PopState();
        FinalizeMesh(MeshParts.All);
    }
}

/// <summary>
/// Runs once at game startup to darken the world-view sky background.
///
/// Vanilla sets <c>WorldSkyboxCamera.backgroundColor</c> to
/// <c>(0.063, 0.090, 0.118)</c> — a noticeable blue-grey.  We replace it
/// with near-black to match the look of deep space.
/// </summary>
[StaticConstructorOnStartup]
static class SkyBackgroundFix
{
    static SkyBackgroundFix()
    {
        // Accessing WorldSkyboxCamera will trigger WorldCameraManager's own
        // static constructor if it hasn't run yet (safe on the main thread).
        var cam = WorldCameraManager.WorldSkyboxCamera;
        if (cam != null)
            cam.backgroundColor = new Color(0.008f, 0.008f, 0.014f, 1f);
    }
}
