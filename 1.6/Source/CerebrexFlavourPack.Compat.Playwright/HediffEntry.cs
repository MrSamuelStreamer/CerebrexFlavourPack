using Verse;

namespace CerebrexFlavourPack.Compat.Playwright;

/// <summary>
/// One scenario-editor-configured hediff: what to add, where on the body, and how severe.
/// Resolved into a <see cref="HediffApplication"/> (with a rolled severity) per spawned animal.
/// </summary>
public class HediffEntry : IExposable
{
    public HediffDef Hediff;
    public BodyPartDef BodyPart;
    public BodySide Side = BodySide.Random;
    public FloatRange SeverityRange = new FloatRange(0.5f, 0.5f);

    public string HediffLabel => Hediff != null ? Hediff.LabelCap : "-";

    public string BodyPartLabel => BodyPart != null ? BodyPart.LabelCap : "CFP_Compat_Playwright_WholeBody".Translate();

    public string SideLabel => ("CFP_Compat_Playwright_Side_" + Side).Translate();

    public float MaxSeverity => Hediff != null && Hediff.lethalSeverity > 0f ? Hediff.lethalSeverity * 0.99f : 1f;

    public void ClampSeverity()
    {
        if (SeverityRange.max > MaxSeverity) SeverityRange.max = MaxSeverity;
        if (SeverityRange.min > MaxSeverity) SeverityRange.min = MaxSeverity;
    }

    public void ExposeData()
    {
        Scribe_Defs.Look(ref Hediff, "hediff");
        Scribe_Defs.Look(ref BodyPart, "bodyPart");
        Scribe_Values.Look(ref Side, "side");
        Scribe_Values.Look(ref SeverityRange, "severityRange");
    }

    public override int GetHashCode()
    {
        return (Hediff != null ? Hediff.GetHashCode() : 0) ^ (BodyPart != null ? BodyPart.GetHashCode() : 0) ^ Side.GetHashCode() ^ SeverityRange.GetHashCode();
    }
}
