using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace CerebrexFlavourPack;

public class CompProperties_PrisonerProcessor : CompProperties
{
    public int ticksToKill = GenDate.TicksPerDay;
    public float bloodFilthMultiplier = 5f;
    public List<ThingDefCountClass> products = new();

    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string item in base.ConfigErrors(parentDef))
        {
            yield return item;
        }
        if (parentDef.tickerType != TickerType.Normal)
        {
            yield return GetType().Name + " requires parent ticker type Normal";
        }
    }
    public CompProperties_PrisonerProcessor()
    {
        compClass = typeof(CompPrisonerProcessor);
    }
}

public class CompPrisonerProcessor : ThingComp, IThingHolder
{
    private ThingOwner innerContainer;
    private CompPowerTrader powerComp;
    private int ticksHeld;
    private Sustainer activeSustainer;
    private int nextDamageTick;
    private readonly List<Hediff_Injury> myInjuries = new();

    private static Graphic slicersStaticGraphic;
    private static Graphic slicersBlur1Graphic;
    private static Graphic slicersBlur2Graphic;

    private static Graphic SlicersStatic => slicersStaticGraphic ??= GraphicDatabase.Get<Graphic_Single>(
        "Building/PeelerSlicersOnly", ShaderDatabase.Transparent, new Vector2(3f, 3f), Color.white);
    private static Graphic SlicersBlur1 => slicersBlur1Graphic ??= GraphicDatabase.Get<Graphic_Single>(
        "Building/PeelerSlicersOnlyBlur1", ShaderDatabase.Transparent, new Vector2(3f, 3f), Color.white);
    private static Graphic SlicersBlur2 => slicersBlur2Graphic ??= GraphicDatabase.Get<Graphic_Single>(
        "Building/PeelerSlicersOnlyBlur2", ShaderDatabase.Transparent, new Vector2(3f, 3f), Color.white);

    public CompProperties_PrisonerProcessor Props => (CompProperties_PrisonerProcessor)props;
    public Pawn Occupant => innerContainer.Count > 0 ? innerContainer[0] as Pawn : null;
    public bool PowerOn => powerComp?.PowerOn == true;
    public bool Empty => Occupant == null;
    public Thing ParentThing => parent;

    public CompPrisonerProcessor()
    {
        innerContainer = new ThingOwner<Thing>(this);
    }

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        powerComp = parent.GetComp<CompPowerTrader>();
    }

    public ThingOwner GetDirectlyHeldThings() => innerContainer;

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, innerContainer);
    }

    public override void CompTickInterval(int delta)
    {
        base.CompTickInterval(delta);

        Pawn occupant = Occupant;
        bool active = occupant != null && PowerOn;

        UpdateSustainer(active);

        if (!active)
            return;

        if (Find.TickManager.TicksGame >= nextDamageTick)
        {
            ApplyProcessingDamage(occupant);
            nextDamageTick = Find.TickManager.TicksGame + Rand.RangeInclusive(30, 90);
        }

        ticksHeld += delta;
        if (ticksHeld >= Props.ticksToKill)
            KillOccupantAndProduce();
    }

    private void ApplyProcessingDamage(Pawn occupant)
    {
        if (occupant == null || occupant.Dead)
        {
            myInjuries.Clear();
            return;
        }

        // Heal any of OUR previously-applied cuts to keep hediff count bounded.
        // Player-inflicted or other injuries are left alone.
        for (int i = myInjuries.Count - 1; i >= 0; i--)
        {
            Hediff_Injury inj = myInjuries[i];
            if (inj == null || inj.pawn == null || inj.pawn.Dead || !inj.pawn.health.hediffSet.hediffs.Contains(inj))
            {
                myInjuries.RemoveAt(i);
                continue;
            }
            inj.pawn.health.RemoveHediff(inj);
            myInjuries.RemoveAt(i);
        }

        // Let the game pick a valid hittable outer body part using its own weighted selector.
        BodyPartRecord part = occupant.health.hediffSet
            .GetRandomNotMissingPart(DamageDefOf.Cut, BodyPartHeight.Undefined, BodyPartDepth.Outside);
        if (part == null)
            return;

        // Keep the pawn's cached position on the machine so any effecter spawned
        // by TakeDamage renders at the machine, not the pre-capture cell.
        SnapPositionToParent(occupant);

        DamageInfo dinfo = new(DamageDefOf.Cut, 0.1f, armorPenetration: 0f, angle: -1f, instigator: parent, hitPart: part);
        DamageWorker.DamageResult result = occupant.TakeDamage(dinfo);
        if (result?.hediffs == null)
            return;

        foreach (Hediff h in result.hediffs)
        {
            if (h is Hediff_Injury inj)
                myInjuries.Add(inj);
        }
    }

    private void UpdateSustainer(bool active)
    {
        if (active)
        {
            if (parent.Spawned && (activeSustainer == null || activeSustainer.Ended))
            {
                activeSustainer = CerebrexFlavourPackDefOf.CFP_HumanProcessor_Ambient
                    .TrySpawnSustainer(SoundInfo.InMap(parent, MaintenanceType.PerTickRare));
            }
            activeSustainer?.Maintain();
        }
        else
        {
            EndSustainer();
        }
    }

    private void EndSustainer()
    {
        if (activeSustainer != null && !activeSustainer.Ended)
            activeSustainer.End();
        activeSustainer = null;
    }

    public bool CanAcceptPawn(Pawn pawn) => CanAcceptPawn(pawn, out _);

    public bool CanAcceptPawn(Pawn pawn, out string failReason)
    {
        failReason = null;

        if (pawn == null)
        {
            failReason = "No pawn.";
            return false;
        }
        if (!Empty)
        {
            failReason = "Building is already occupied.";
            return false;
        }
        if (!PowerOn)
        {
            failReason = "Building is not powered.";
            return false;
        }
        if (!pawn.RaceProps.Humanlike)
        {
            failReason = "Pawn must be human-like.";
            return false;
        }
        if (pawn.Dead)
        {
            failReason = "Pawn must be alive.";
            return false;
        }
        if (!pawn.IsPrisonerOfColony)
        {
            failReason = "Pawn must be a prisoner.";
            return false;
        }
        return true;
    }

    public bool TryAcceptPawn(Pawn pawn)
    {
        if (!CanAcceptPawn(pawn))
            return false;

        ticksHeld = 0;
        nextDamageTick = Find.TickManager.TicksGame + Rand.RangeInclusive(30, 90);
        myInjuries.Clear();
        bool wasSelected = pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
        if (pawn.holdingOwner != null)
        {
            pawn.holdingOwner.TryTransferToContainer(pawn, innerContainer, false);
        }
        else
        {
            innerContainer.TryAdd(pawn, false);
        }

        // Move the pawn's cached position onto the machine so damage effecters
        // (which read pawn.Position directly, not PositionHeld) spawn their
        // visual FX on the machine instead of the pre-capture cell.
        SnapPositionToParent(pawn);

        if (wasSelected)
            Find.Selector.Select(parent, playSound: false, forceDesignatorDeselect: false);

        return true;
    }

    private static readonly System.Reflection.FieldInfo PositionIntField =
        typeof(Thing).GetField("positionInt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    private void SnapPositionToParent(Thing pawn)
    {
        if (PositionIntField == null || pawn == null || pawn.Spawned)
            return;
        PositionIntField.SetValue(pawn, parent.Position);
    }

    public override string CompInspectStringExtra()
    {
        Pawn occupant = Occupant;
        if (occupant == null)
            return null;

        int remainingTicks = Mathf.Max(Props.ticksToKill - ticksHeld, 0);
        List<string> lines = new()
        {
            $"Occupant: {occupant.LabelShortCap}",
            $"Processing time remaining: {remainingTicks.ToStringTicksToPeriod()}"
        };

        if (!PowerOn)
            lines.Add("Paused: unpowered");

        return string.Join("\n", lines);
    }

    public override void PostDraw()
    {
        base.PostDraw();

        Pawn occupant = Occupant;
        bool active = occupant != null && PowerOn;

        Graphic overlay;
        if (active)
        {
            int frame = (Find.TickManager.TicksGame / 4) % 2;
            overlay = frame == 0 ? SlicersBlur1 : SlicersBlur2;
        }
        else
        {
            overlay = SlicersStatic;
        }

        Vector3 slicerPos = parent.DrawPos;
        slicerPos.y = AltitudeLayer.BuildingOnTop.AltitudeFor();
        overlay.Draw(slicerPos, Rot4.North, parent);

        if (occupant == null)
            return;

        Vector3 drawPos = parent.DrawPos + new Vector3(0f, 0.1f, 0f);
        occupant.Drawer.renderer.RenderPawnAt(drawPos, Rot4.South, neverAimWeapon: true);
    }

    public override void PostSpawnSetup(bool respawningAfterLoad)
    {
        base.PostSpawnSetup(respawningAfterLoad);
        parent.Map.GetComponent<PrisonerProcessorTracker>()?.Register(this);
    }
    public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
    {
        map.GetComponent<PrisonerProcessorTracker>()?.Deregister(this);
        EndSustainer();

        if (mode != DestroyMode.WillReplace)
            EjectContents(map);

        base.PostDeSpawn(map, mode);
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        EndSustainer();
        EjectContents(previousMap);
        base.PostDestroy(mode, previousMap);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_Values.Look(ref ticksHeld, "ticksHeld");
        Scribe_Values.Look(ref nextDamageTick, "nextDamageTick");

        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            powerComp = parent.GetComp<CompPowerTrader>();
    }

    private void KillOccupantAndProduce()
    {
        Pawn occupant = Occupant;
        if (occupant == null)
            return;
        Map map = parent.MapHeld;

        if (map == null)
            return;

        IntVec3 dropCell = parent.InteractionCell.IsValid ? parent.InteractionCell : parent.PositionHeld;
        FilthMaker.TryMakeFilth(dropCell, map, ThingDefOf.Filth_Blood, occupant.LabelIndefinite(), Mathf.CeilToInt(occupant.BodySize * Props.bloodFilthMultiplier));
        string label = occupant.LabelShortCap;

        occupant.Kill(null);
        innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);
        ticksHeld = 0;
        myInjuries.Clear();
        int lostCount = 0;


        foreach (ThingDefCountClass product in Props.products)
        {
        if (product?.thingDef == null || product.count <= 0)
            continue;

            int remaining = product.count;
            while (remaining > 0)
            {
                Thing thing = ThingMaker.MakeThing(product.thingDef);
                thing.stackCount = Mathf.Min(remaining, Mathf.Max(product.thingDef.stackLimit, 1));
                remaining -= thing.stackCount;

                if (!GenPlace.TryPlaceThing(thing, dropCell, map, ThingPlaceMode.Near))
                {
                    lostCount += thing.stackCount;
                    thing.Destroy();
                }
            }
        }

        if (lostCount > 0)
        {
            Log.Error($"[CFP Human Processor] Failed to place human processor output - destroying to avoid orphaned object references.");
        }
        Messages.Message("CFP_PrisonerProcessor_Complete".Translate(label), parent, MessageTypeDefOf.PositiveEvent, historical: true);
    }

    private void EjectContents(Map map)
    {
        if (map == null || innerContainer.Count == 0)
            return;

        IntVec3 dropCell = parent.InteractionCell.IsValid ? parent.InteractionCell : parent.PositionHeld;
        innerContainer.TryDropAll(dropCell, map, ThingPlaceMode.Near);
        ticksHeld = 0;
        myInjuries.Clear();
    }
}
