using System.Collections.Generic;
using System.Linq;
using BigAndSmall;
using RimWorld;
using Rokk.Playwright.Compat.BSSapientAnimals.Things;
using Verse;

namespace CerebrexFlavourPack.Compat.Playwright;

/// <summary>
/// Identical to <see cref="DelayedSapientAnimalSpawner"/>, except it also applies configured
/// hediffs to the resulting pawn once the sapient swap completes.
/// </summary>
/// <remarks>
/// <see cref="RaceMorpher.SwapAnimalToSapientVersion"/> destroys the original animal pawn and
/// returns a brand-new one - the base class's <c>MakeAnimalSapient</c> discards that return
/// value, leaving <c>SpawnedAnimal</c> pointing at a destroyed pawn. We capture it here so the
/// hediffs target the pawn that actually survives the swap. The swap can also legitimately fail
/// and return null (e.g. no humanlike counterpart registered for that race - mechanoids need
/// Big and Small's "Enable Sapient Mechanoids" setting turned on, with a restart), which we
/// treat as a no-op rather than a crash.
/// </remarks>
public class DelayedSapientAnimalSpawnerWithHediff : DelayedSapientAnimalSpawner
{
    public List<HediffApplication> PendingHediffs = new List<HediffApplication>();

    protected override void MakeAnimalSapient()
    {
        SpawnedAnimal = RaceMorpher.SwapAnimalToSapientVersion(SpawnedAnimal);

        if (SpawnedAnimal?.health == null)
        {
            Log.Warning($"[CerebrexFlavourPack.Compat.Playwright] Could not make {AnimalKind?.defName} sapient - Big and Small found no humanlike counterpart for it. Mechanoids need \"Enable Sapient Mechanoids\" turned on in Big and Small's mod settings (restart required). Skipping {PendingHediffs.Count} pending hediff(s).");
            return;
        }

        foreach (HediffApplication application in PendingHediffs)
        {
            ApplyHediff(SpawnedAnimal, application);
        }
    }

    private static void ApplyHediff(Pawn pawn, HediffApplication application)
    {
        BodyPartRecord part = null;
        if (application.BodyPart != null)
        {
            List<BodyPartRecord> matches = pawn.RaceProps.body.GetPartsWithDef(application.BodyPart).ToList();
            if (matches.Count == 0)
            {
                Log.Warning($"[CerebrexFlavourPack.Compat.Playwright] {pawn} has no {application.BodyPart.defName} to apply {application.Hediff.defName} to; applying to the whole body instead.");
            }
            else
            {
                part = application.Side switch
                {
                    BodySide.Left => matches[0],
                    BodySide.Right => matches[matches.Count - 1],
                    _ => Rand.Bool ? matches[0] : matches[matches.Count - 1]
                };
            }
        }

        Hediff hediff = HediffMaker.MakeHediff(application.Hediff, pawn, part);
        hediff.Severity = application.Severity;
        pawn.health.AddHediff(hediff, part);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref PendingHediffs, "pendingHediffs", LookMode.Deep);
        PendingHediffs ??= new List<HediffApplication>();
    }
}
