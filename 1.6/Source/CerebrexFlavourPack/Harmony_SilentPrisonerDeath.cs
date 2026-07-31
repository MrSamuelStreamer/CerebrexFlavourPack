using HarmonyLib;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack
{
    [HarmonyPatch(typeof(Messages), nameof(Messages.Message), typeof(Message), typeof(bool))]
    public static class Patch_SilenceHumanDeathSound
    {
        private static readonly MessageTypeDef SilentPawnDeath = new MessageTypeDef { sound = null };

        public static void Prefix(Message msg)
        {
            if (msg.def != MessageTypeDefOf.PawnDeath)
                return;

            if (msg.lookTargets.TryGetPrimaryTarget().Thing is Pawn victim && victim.RaceProps.Humanlike)
            {
                msg.def = SilentPawnDeath;
            }
        }
    }
}
