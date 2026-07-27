using RimWorld;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Mood from living inside (or beside) the mechanoid lattice. Reads a coarse density grid on the
/// map's <see cref="MapComponent_CerebrexLattice"/> rather than scanning cells directly, and
/// deliberately ignores line of sight - the buff is meant to reach indoors, where player floors
/// keep the ground itself permanently unconverted.
/// </summary>
public class ThoughtWorker_CerebrexLattice : ThoughtWorker
{
    private const int Radius = 12;

    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!CerebrexLatticeDefs.Active || !p.Spawned)
        {
            return ThoughtState.Inactive;
        }

        MapComponent_CerebrexLattice lattice = p.Map.GetComponent<MapComponent_CerebrexLattice>();
        if (lattice == null)
        {
            return ThoughtState.Inactive;
        }

        float frac = lattice.ConvertedFractionNear(p.Position, Radius);
        if (frac < 0.10f)
        {
            return ThoughtState.Inactive;
        }

        if (frac < 0.35f)
        {
            return ThoughtState.ActiveAtStage(0);
        }

        if (frac < 0.65f)
        {
            return ThoughtState.ActiveAtStage(1);
        }

        if (frac < 0.90f)
        {
            return ThoughtState.ActiveAtStage(2);
        }

        // The top stage is only reachable once the biome itself has flipped - it's meant to
        // partly pay back the hunting/fishing/foraging economy that the flip destroys.
        return ThoughtState.ActiveAtStage(lattice.BiomeFlipped ? 4 : 3);
    }

    public override float MoodMultiplier(Pawn p)
    {
        return Mathf.Clamp(CerebrexFlavourPackMod.settings?.moodBuffScale ?? 1f, 0f, 2f);
    }
}
