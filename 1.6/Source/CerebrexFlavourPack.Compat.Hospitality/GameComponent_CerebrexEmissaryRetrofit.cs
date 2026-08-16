using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace CerebrexFlavourPack.Compat.Hospitality;

/// <summary>
/// Adds the Cerebrex Emissary Circuit faction to saves that were started
/// before this compat DLL existed.
///
/// Flow:
/// <list type="number">
///   <item>On <see cref="LoadedGame"/>, if <c>MSS_CF_CerebrexEmissary</c> is
///     already in <see cref="FactionManager.AllFactionsListForReading"/>,
///     do nothing.</item>
///   <item>Otherwise defer the actual inject by one tick via
///     <see cref="LongEventHandler.QueueLongEvent(System.Action, string, bool, System.Action{System.Exception})"/>
///     so the world is fully loaded before we touch it.</item>
///   <item>Wrap
///     <see cref="FactionGenerator.NewGeneratedFaction(FactionGeneratorParms)"/>
///     in try/catch. On failure, log and set <see cref="retrofitAttempted"/>
///     so we do not retry within the same session.</item>
///   <item>Settlement placement is best-effort. If
///     <see cref="TileFinder.TryFindNewSiteTile"/> returns false, the
///     faction is still registered but no world object is added. Visitor
///     incidents fire from goodwill, not from settlement presence.</item>
/// </list>
///
/// Users can retry a failed retrofit through the dev-mode debug action in
/// <see cref="DebugActions_CerebrexEmissary"/>.
/// </summary>
public class GameComponent_CerebrexEmissaryRetrofit : GameComponent
{
    private const string FactionDefName = "MSS_CF_CerebrexEmissary";
    private const string LoadEventLabel = "MSS_CF.Retrofit.CerebrexEmissary";

    /// <summary>
    /// Set true after the first retrofit attempt in the current session so
    /// a failed attempt does not spam the log every time the user reloads
    /// the same broken save. Not serialised, so a fresh session gets a fresh
    /// attempt.
    /// </summary>
    private bool retrofitAttempted;

    public GameComponent_CerebrexEmissaryRetrofit(Game game)
    {
    }

    public override void LoadedGame()
    {
        base.LoadedGame();
        TryRetrofitDeferred();
    }

    public override void StartedNewGame()
    {
        base.StartedNewGame();
        // A new game should already have generated the faction if it can
        // (via requiredCountAtGameStart / canMakeRandomly). Only retrofit
        // if something in scenario/mod ordering skipped it.
        TryRetrofitDeferred();
    }

    private void TryRetrofitDeferred()
    {
        if (retrofitAttempted)
        {
            return;
        }

        if (FactionAlreadyPresent())
        {
            retrofitAttempted = true;
            return;
        }

        // Defer by one long-event tick so the world/faction manager are
        // fully materialised before we mutate them.
        LongEventHandler.QueueLongEvent(RunRetrofit, LoadEventLabel, false, null);
    }

    private void RunRetrofit()
    {
        retrofitAttempted = true;

        if (FactionAlreadyPresent())
        {
            return;
        }

        FactionDef def = DefDatabase<FactionDef>.GetNamedSilentFail(FactionDefName);
        if (def == null)
        {
            Log.Message("[CFP.Hospitality] Cerebrex Emissary faction def missing; " +
                        "compat DLL loaded but its defs did not. Check loadFolders wiring.");
            return;
        }

        Faction newFaction;
        try
        {
            newFaction = FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));
        }
        catch (Exception ex)
        {
            Log.Error("[CFP.Hospitality] Failed to generate Cerebrex Emissary faction: " + ex);
            return;
        }

        try
        {
            Find.FactionManager.Add(newFaction);
            InitialiseRelations(newFaction);
        }
        catch (Exception ex)
        {
            Log.Error("[CFP.Hospitality] Failed to register Cerebrex Emissary faction with " +
                      "FactionManager: " + ex);
            return;
        }

        TryPlaceSettlement(newFaction);

        Messages.Message(
            "The Cerebrex Emissary Circuit has opened a diplomatic channel with your colony.",
            MessageTypeDefOf.NeutralEvent, false);
    }

    private static bool FactionAlreadyPresent()
    {
        if (Find.FactionManager == null)
        {
            return false;
        }

        foreach (Faction f in Find.FactionManager.AllFactionsListForReading)
        {
            if (f.def != null && f.def.defName == FactionDefName)
            {
                return true;
            }
        }
        return false;
    }

    private static void InitialiseRelations(Faction faction)
    {
        // FactionGenerator produces a faction whose FactionRelation table
        // does not include factions that already existed in the save.
        // Seed a neutral baseline against every existing faction so
        // Hospitality's goodwill checks have a value to read.
        List<Faction> existing = Find.FactionManager.AllFactionsListForReading.ToList();
        foreach (Faction other in existing)
        {
            if (other == faction || other.IsPlayer)
            {
                continue;
            }
            faction.TryMakeInitialRelationsWith(other);
        }

        // Give the player a small positive nudge so the first visitor
        // group can fire from natural goodwill without an event lottery.
        FactionRelation playerRel = faction.RelationWith(Faction.OfPlayer, true);
        if (playerRel != null)
        {
            playerRel.baseGoodwill = 20;
        }
    }

    private static void TryPlaceSettlement(Faction faction)
    {
        try
        {
            if (Find.WorldGrid == null)
            {
                return;
            }

            if (!TileFinder.TryFindNewSiteTile(
                    out PlanetTile tile,
                    minDist: 8,
                    maxDist: 30,
                    allowCaravans: false))
            {
                Log.Message("[CFP.Hospitality] No valid tile for Cerebrex Emissary settlement; " +
                            "faction registered without a home base. Visitors still fire from goodwill.");
                return;
            }

            Settlement settlement =
                (Settlement)WorldObjectMaker.MakeWorldObject(WorldObjectDefOf.Settlement);
            settlement.SetFaction(faction);
            settlement.Tile = tile;
            settlement.Name = SettlementNameGenerator.GenerateSettlementName(settlement);
            Find.WorldObjects.Add(settlement);
        }
        catch (Exception ex)
        {
            Log.Warning("[CFP.Hospitality] Cerebrex Emissary settlement placement failed " +
                        "(faction still registered): " + ex);
        }
    }

    /// <summary>
    /// Public entry point used by the dev-mode debug action so the user can
    /// retry a failed retrofit without reloading their save.
    /// </summary>
    public void ForceRetrofit()
    {
        retrofitAttempted = false;
        TryRetrofitDeferred();
    }

    public override void ExposeData()
    {
        base.ExposeData();
        // Deliberately not saving retrofitAttempted. A save that failed to
        // retrofit last session should get a clean attempt on next load.
    }
}
