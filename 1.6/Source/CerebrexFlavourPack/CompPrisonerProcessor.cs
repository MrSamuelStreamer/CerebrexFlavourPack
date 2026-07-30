using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack;

public class CompProperties_PrisonerProcessor : CompProperties
{
    public int ticksToKill = GenDate.TicksPerDay;
    public float bloodFilthMultiplier = 5f;
    public List<ThingDefCountClass> products = new();

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

        if (occupant == null || !PowerOn)
            return;

        ticksHeld += delta;
        if (ticksHeld >= Props.ticksToKill)
            KillOccupantAndProduce();
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
        bool wasSelected = pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
        if (pawn.holdingOwner != null)
        {
            pawn.holdingOwner.TryTransferToContainer(pawn, innerContainer, false);
        }
        else
        {
            innerContainer.TryAdd(pawn, false);
        }

        if (wasSelected)
            Find.Selector.Select(parent, playSound: false, forceDesignatorDeselect: false);

        return true;
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

        if (mode != DestroyMode.WillReplace)
            EjectContents(map);

        base.PostDeSpawn(map, mode);
    }

    public override void PostDestroy(DestroyMode mode, Map previousMap)
    {
        EjectContents(previousMap);
        base.PostDestroy(mode, previousMap);
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_Values.Look(ref ticksHeld, "ticksHeld");

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
    }
}
