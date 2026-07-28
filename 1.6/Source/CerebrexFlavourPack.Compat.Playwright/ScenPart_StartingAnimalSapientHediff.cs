using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Rokk.Playwright.Compat.BSSapientAnimals.ScenParts;
using Rokk.Playwright.UI;
using Rokk.Playwright.Utilities;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack.Compat.Playwright;

/// <summary>
/// Identical to <see cref="StartingAnimalSapient"/>, except it also lets the scenario editor
/// configure one or more hediffs (each with its own body part, side, and severity range) to
/// apply to the sapient animal once it's spawned.
/// </summary>
public class ScenPart_StartingAnimalSapientHediff : StartingAnimalSapient
{
    public List<HediffEntry> Hediffs = new List<HediffEntry>();

    protected override IEnumerable<PawnKindDef> GetAllowedAnimals()
    {
        return DefDatabase<PawnKindDef>.AllDefs.Where(pawnKindDef => pawnKindDef.RaceProps.Animal || pawnKindDef.RaceProps.IsMechanoid);
    }

    private static IEnumerable<HediffDef> PossibleHediffs()
    {
        return DefDatabase<HediffDef>.AllDefsListForReading;
    }

    // Rows: animal picker, count label, count field, "add hediff" button, skip, help button,
    // plus per-entry rows (hediff+remove, body part, side [only if a specific part is chosen],
    // severity). Listing_ScenEdit.GetScenPartRect reserves one rect sized for this scenpart's
    // *entire* UI in one call, so the row count has to be computed up front.
    private int TotalRows()
    {
        int rows = 6;
        foreach (HediffEntry entry in Hediffs)
        {
            rows += entry.BodyPart != null ? 4 : 3;
        }
        return rows;
    }

    public override void DoEditInterface(Listing_ScenEdit listing)
    {
        int rows = TotalRows();
        Rect scenPartRect = listing.GetScenPartRect(this, RowHeight * rows);
        var helper = new ScenPartDrawHelper(scenPartRect, RowHeight, rows);

        // Animal selector
        if (Widgets.ButtonText(helper.NextRect(), AnimalKindLabel))
        {
            var options = new List<FloatMenuOption>()
            {
                new FloatMenuOption("RandomPet".Translate().CapitalizeFirst(), () => AnimalKind = null)
            };
            foreach (PawnKindDef animalDef in GetAllowedAnimals())
            {
                options.Add(new FloatMenuOption(animalDef.LabelCap, () => AnimalKind = animalDef));
            }
            PlaywrightUtils.OpenFloatMenu(options);
            SoundUtils.PlayClick();
        }

        Widgets.Label(helper.NextRect(), "Playwright.Count".Translate());
        Widgets.IntEntry(helper.NextRect(), ref Count, ref CountBuffer);
        if (Count < 0)
        {
            Count = 0;
            CountBuffer = "0";
        }

        if (Widgets.ButtonText(helper.NextRect(), "CFP_Compat_Playwright_AddHediff".Translate()))
        {
            Hediffs.Add(new HediffEntry());
        }

        HediffEntry toRemove = null;
        for (int i = 0; i < Hediffs.Count; i++)
        {
            HediffEntry entry = Hediffs[i];

            Rect hediffRowRect = helper.NextRect();
            var removeButtonRect = new Rect(hediffRowRect.xMax - RowHeight, hediffRowRect.y, RowHeight, hediffRowRect.height);
            var hediffButtonRect = new Rect(hediffRowRect.x, hediffRowRect.y, hediffRowRect.width - RowHeight - 4f, hediffRowRect.height);

            if (Widgets.ButtonText(hediffButtonRect, entry.HediffLabel))
            {
                var options = new List<FloatMenuOption>();
                foreach (HediffDef hediffDef in PossibleHediffs())
                {
                    options.Add(new FloatMenuOption(hediffDef.LabelCap, () =>
                    {
                        entry.Hediff = hediffDef;
                        entry.ClampSeverity();
                    }));
                }
                PlaywrightUtils.OpenFloatMenu(options);
                SoundUtils.PlayClick();
            }
            if (Widgets.ButtonText(removeButtonRect, "X"))
            {
                toRemove = entry;
            }

            if (Widgets.ButtonText(helper.NextRect(), entry.BodyPartLabel))
            {
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption("CFP_Compat_Playwright_WholeBody".Translate(), () => entry.BodyPart = null)
                };
                foreach (BodyPartDef bodyPartDef in DefDatabase<BodyPartDef>.AllDefsListForReading.OrderBy(b => b.LabelCap.ToString()))
                {
                    options.Add(new FloatMenuOption(bodyPartDef.LabelCap, () => entry.BodyPart = bodyPartDef));
                }
                PlaywrightUtils.OpenFloatMenu(options);
                SoundUtils.PlayClick();
            }

            if (entry.BodyPart != null)
            {
                if (Widgets.ButtonText(helper.NextRect(), entry.SideLabel))
                {
                    var options = new List<FloatMenuOption>
                    {
                        new FloatMenuOption("CFP_Compat_Playwright_Side_Left".Translate(), () => entry.Side = BodySide.Left),
                        new FloatMenuOption("CFP_Compat_Playwright_Side_Right".Translate(), () => entry.Side = BodySide.Right),
                        new FloatMenuOption("CFP_Compat_Playwright_Side_Random".Translate(), () => entry.Side = BodySide.Random)
                    };
                    PlaywrightUtils.OpenFloatMenu(options);
                    SoundUtils.PlayClick();
                }
            }

            // XOR with the loop index: our internal rows don't otherwise advance
            // listing.CurHeight, so every entry in this list would collide on the same
            // FloatRange drag-state id without it.
            Widgets.FloatRange(helper.NextRect(), listing.CurHeight.GetHashCode() ^ i, ref entry.SeverityRange, 0f, entry.MaxSeverity, "ConfigurableSeverity");
        }
        if (toRemove != null)
        {
            Hediffs.Remove(toRemove);
        }

        helper.Skip();

        if (Widgets.ButtonText(helper.NextRect(), "?"))
        {
            Find.WindowStack.Add(new InfoPopupWindow("CFP_Compat_Playwright_StartingAnimalSapientHediff_Help".Translate()));
        }
    }

    protected override void SpawnDelayedSpawner(Map map, PawnKindDef animalDef)
    {
        ThingDef spawnerDef = CFPCompatPlaywrightDefOf.CFP_DelayedSapientAnimalSpawnerWithHediff;
        var spawner = (DelayedSapientAnimalSpawnerWithHediff)ThingMaker.MakeThing(spawnerDef);
        spawner.AnimalKind = animalDef;
        spawner.PendingHediffs = Hediffs
            .Where(entry => entry.Hediff != null)
            .Select(entry => new HediffApplication
            {
                Hediff = entry.Hediff,
                BodyPart = entry.BodyPart,
                Side = entry.Side,
                Severity = entry.SeverityRange.RandomInRange
            })
            .ToList();

        IntVec3 spawnLoc = CellFinder.RandomClosewalkCellNear(map.Center, map, 10);
        GenSpawn.Spawn(spawner, spawnLoc, map, Rot4.Random);
    }

    public override IEnumerable<string> GetSummaryListEntries(string tag)
    {
        if (tag == "PlayerStartsWith")
        {
            string hediffList = Hediffs.Count > 0
                ? string.Join(", ", Hediffs.Where(entry => entry.Hediff != null).Select(entry => entry.HediffLabel))
                : "-";
            yield return "CFP_Compat_Playwright_StartingAnimalSapientHediff_Summary".Translate(AnimalKindLabel, Count.ToString(), hediffList);
        }
    }

    public override bool HasNullDefs()
    {
        return base.HasNullDefs() || Hediffs.Any(entry => entry.Hediff == null);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref Hediffs, "hediffs", LookMode.Deep);
        Hediffs ??= new List<HediffEntry>();
    }

    public override int GetHashCode()
    {
        int hash = base.GetHashCode();
        foreach (HediffEntry entry in Hediffs)
        {
            hash ^= entry.GetHashCode();
        }
        return hash;
    }
}
