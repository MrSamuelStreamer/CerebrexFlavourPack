using LudeonTK;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack.Compat.Hospitality;

/// <summary>
/// Dev-mode manual retry for the emissary faction retrofit. Only shows up
/// when Dev Mode is on. Recovery path for the case where
/// <see cref="GameComponent_CerebrexEmissaryRetrofit"/> failed on
/// world-load and the faction never got created.
///
/// Uses PlayingOnMap only. PlayingOnWorld is deliberately omitted because
/// RimWorld's AllowedGameStates flags are AND-combined at runtime, so
/// <c>PlayingOnWorld | PlayingOnMap</c> would hide the entry entirely.
/// </summary>
public static class DebugActions_CerebrexEmissary
{
    [DebugAction(
        category: "MSS Cerebrex Flavour Pack",
        name: "Retrofit: force spawn Cerebrex Emissary faction",
        allowedGameStates = AllowedGameStates.Playing)]
    public static void ForceSpawnCerebrexEmissary()
    {
        Game game = Current.Game;
        if (game == null)
        {
            return;
        }

        GameComponent_CerebrexEmissaryRetrofit comp =
            game.GetComponent<GameComponent_CerebrexEmissaryRetrofit>();
        if (comp == null)
        {
            Log.Warning("[CFP.Hospitality] Retrofit GameComponent missing from current game.");
            return;
        }

        comp.ForceRetrofit();
    }
}
