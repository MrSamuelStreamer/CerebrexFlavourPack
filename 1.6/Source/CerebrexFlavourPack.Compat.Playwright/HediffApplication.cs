using Verse;

namespace CerebrexFlavourPack.Compat.Playwright;

/// <summary>
/// A <see cref="HediffEntry"/> resolved for one specific spawned animal - severity already rolled.
/// Lives on <see cref="DelayedSapientAnimalSpawnerWithHediff"/> rather than the ScenPart so that
/// spawning several animals from one ScenPart instance (Count > 1) rolls severity independently
/// per animal.
/// </summary>
public class HediffApplication : IExposable
{
    public HediffDef Hediff;
    public BodyPartDef BodyPart;
    public BodySide Side;
    public float Severity;

    public void ExposeData()
    {
        Scribe_Defs.Look(ref Hediff, "hediff");
        Scribe_Defs.Look(ref BodyPart, "bodyPart");
        Scribe_Values.Look(ref Side, "side");
        Scribe_Values.Look(ref Severity, "severity");
    }
}
