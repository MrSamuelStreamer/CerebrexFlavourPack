using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

/// <summary>
/// Watches for the loss of the player's last cerebrex core and shuts the whole swarm down.
/// </summary>
public class GameComponent_CerebrexWatch : GameComponent
{
    private bool shutdownTriggered;

    public GameComponent_CerebrexWatch(Game game)
    {
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref shutdownTriggered, "shutdownTriggered", false);
    }

    /// <summary>Called from CompCerebrexCore.PostDestroy, after the core has already despawned.</summary>
    public void Notify_CoreDestroyed(Thing core, DestroyMode mode, Map previousMap)
    {
        // Only an actual kill counts. Map removal, abandonment and gravship relocation all destroy
        // buildings with DestroyMode.Vanish, and none of those should end the run.
        if (mode != DestroyMode.KillFinalize && mode != DestroyMode.KillFinalizeLeavingsOnly)
        {
            return;
        }

        if (shutdownTriggered || core.Faction != Faction.OfPlayer)
        {
            return;
        }

        if (AnyPlayerCoreRemains())
        {
            return;
        }

        ShutDownSwarm();
    }

    private static bool AnyPlayerCoreRemains()
    {
        List<Map> maps = Find.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            if (maps[i].listerBuildings.AllBuildingsColonistOfDef(CerebrexFlavourPackDefOf.CFP_CerebrexCore).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void ShutDownSwarm()
    {
        shutdownTriggered = true;

        int shutDown = 0;
        List<Map> maps = Find.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            // Copy: AddHediff can down or kill a pawn, which mutates AllPawnsSpawned mid-iteration.
            List<Pawn> pawns = new(maps[i].mapPawns.AllPawnsSpawned);
            for (int j = 0; j < pawns.Count; j++)
            {
                Pawn pawn = pawns[j];
                if (pawn.Destroyed || pawn.Dead || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.IsMechanoid)
                {
                    continue;
                }

                if (pawn.health.hediffSet.HasHediff(CerebrexFlavourPackDefOf.CFP_CerebrexShutdown))
                {
                    continue;
                }

                pawn.health.AddHediff(CerebrexFlavourPackDefOf.CFP_CerebrexShutdown);
                shutDown++;
            }
        }

        Find.LetterStack.ReceiveLetter(
            "CerebrexFlavourPack_CoreLost_LetterLabel".Translate(),
            "CerebrexFlavourPack_CoreLost_LetterText".Translate(shutDown),
            LetterDefOf.NegativeEvent);

        // Find.GameEnder only ends the game on colonist count, so the loss screen is driven directly.
        Find.WindowStack.Add(new Dialog_MessageBox(
            "CerebrexFlavourPack_CoreLost_DialogText".Translate(),
            "QuitToMainMenu".Translate(), GenScene.GoToMainMenu,
            "Close".Translate(), null,
            "CerebrexFlavourPack_CoreLost_DialogTitle".Translate()));
    }
}
