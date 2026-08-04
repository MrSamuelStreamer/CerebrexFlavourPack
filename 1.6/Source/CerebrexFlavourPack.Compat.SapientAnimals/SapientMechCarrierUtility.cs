using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace CerebrexFlavourPack.Compat.SapientAnimals;

/// <summary>
/// Utility functions for CompMechCarrier on Big and Small sapient (humanlike) war queens.
/// </summary>
public static class SapientMechCarrierUtility
{
    private static readonly AccessTools.FieldRef<CompMechCarrier, ThingOwner> InnerContainer =
        AccessTools.FieldRefAccess<CompMechCarrier, ThingOwner>("innerContainer");

    private static readonly AccessTools.FieldRef<CompMechCarrier, int> CooldownTicksRemaining =
        AccessTools.FieldRefAccess<CompMechCarrier, int>("cooldownTicksRemaining");

    private static readonly AccessTools.FieldRef<CompMechCarrier, MechCarrierGizmo> CarrierGizmo =
        AccessTools.FieldRefAccess<CompMechCarrier, MechCarrierGizmo>("gizmo");

    /// <summary>
    /// Player-faction humanlike pawn that still has a mech carrier (sapient war queen).
    /// Real mechanoids keep using vanilla CompMechCarrier logic untouched.
    /// </summary>
    public static bool IsSapientPlayerCarrier(Pawn pawn)
    {
        if (pawn?.Faction != Faction.OfPlayer)
            return false;
        if (pawn.RaceProps.IsMechanoid)
            return false;
        return pawn.TryGetComp<CompMechCarrier>() != null;
    }

    public static bool IsSapientPlayerCarrier(CompMechCarrier comp)
    {
        return comp?.parent is Pawn pawn && IsSapientPlayerCarrier(pawn);
    }

    /// <summary>
    /// Create steel storage if the race morph left CompMechCarrier with a null container.
    /// </summary>
    public static void EnsureInnerContainer(CompMechCarrier comp)
    {
        if (comp == null || InnerContainer(comp) != null)
            return;

        var container = new ThingOwner<Thing>(comp, false, LookMode.Deep, true);
        InnerContainer(comp) = container;

        CompProperties_MechCarrier props = comp.Props;
        if (props.startingIngredientCount > 0 && props.fixedIngredient != null)
        {
            Thing steel = ThingMaker.MakeThing(props.fixedIngredient);
            steel.stackCount = props.startingIngredientCount;
            container.TryAdd(steel, props.startingIngredientCount, true);
        }

        if (comp.maxToFill <= 0)
            comp.maxToFill = props.startingIngredientCount;
    }

    public static IEnumerable<Gizmo> GetSapientCarrierGizmos(CompMechCarrier comp)
    {
        EnsureInnerContainer(comp);

        Pawn pawn = (Pawn)comp.parent;
        if (!pawn.IsColonistPlayerControlled)
            yield break;

        if (Find.Selector.SingleSelectedThing == comp.parent)
        {
            if (CarrierGizmo(comp) == null)
                CarrierGizmo(comp) = new MechCarrierGizmo(comp);
            yield return CarrierGizmo(comp);
        }

        AcceptanceReport canSpawn = GetSapientCanSpawn(comp);
        var act = new Command_ActionWithCooldown
        {
            cooldownPercentGetter = () => Mathf.InverseLerp(comp.Props.cooldownTicks, 0f, CooldownTicksRemaining(comp)),
            action = comp.TrySpawnPawns,
            hotKey = KeyBindingDefOf.Misc2,
            Disabled = !canSpawn.Accepted,
            icon = ContentFinder<Texture2D>.Get("UI/Gizmos/ReleaseWarUrchins", true),
            defaultLabel = "MechCarrierRelease".Translate(comp.Props.spawnPawnKind.labelPlural),
            defaultDesc = "MechCarrierDesc".Translate(
                comp.Props.maxPawnsToSpawn,
                comp.Props.spawnPawnKind.labelPlural,
                comp.Props.spawnPawnKind.label,
                comp.Props.costPerPawn,
                comp.Props.fixedIngredient.label)
        };
        if (!canSpawn.Reason.NullOrEmpty())
            act.Disable(canSpawn.Reason);

        if (DebugSettings.ShowDevGizmos)
        {
            if (CooldownTicksRemaining(comp) > 0)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: Reset cooldown",
                    action = () => CooldownTicksRemaining(comp) = 0
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "DEV: Fill with " + comp.Props.fixedIngredient.label,
                action = () =>
                {
                    EnsureInnerContainer(comp);
                    while (comp.IngredientCount < comp.Props.maxIngredientCount)
                    {
                        int stackCount = Mathf.Min(
                            comp.Props.maxIngredientCount - comp.IngredientCount,
                            comp.Props.fixedIngredient.stackLimit);
                        Thing thing = ThingMaker.MakeThing(comp.Props.fixedIngredient);
                        thing.stackCount = stackCount;
                        InnerContainer(comp).TryAdd(thing, thing.stackCount, true);
                    }
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "DEV: Empty " + comp.Props.fixedIngredient.label,
                action = () =>
                {
                    EnsureInnerContainer(comp);
                    InnerContainer(comp).ClearAndDestroyContents(DestroyMode.Vanish);
                }
            };
        }

        yield return act;
    }

    public static AcceptanceReport GetSapientCanSpawn(CompMechCarrier comp)
    {
        EnsureInnerContainer(comp);

        Pawn pawn = comp.parent as Pawn;
        if (pawn != null)
        {
            if (pawn.IsSelfShutdown())
                return "SelfShutdown".Translate();
            if (pawn.Faction == Faction.OfPlayer && !pawn.IsColonistPlayerControlled)
                return false;
            if (!pawn.Awake() || pawn.Downed || pawn.Dead || !pawn.Spawned)
                return false;
        }

        if (comp.MaxCanSpawn <= 0)
            return "MechCarrierNotEnoughResources".Translate();
        if (CooldownTicksRemaining(comp) > 0)
            return "CooldownTime".Translate() + " " + CooldownTicksRemaining(comp).ToStringSecondsFromTicks();
        return true;
    }
}
