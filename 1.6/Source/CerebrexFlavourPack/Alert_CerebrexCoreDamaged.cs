using System.Collections.Generic;
using RimWorld;
using Verse;

namespace CerebrexFlavourPack;

public class Alert_CerebrexCoreDamaged : Alert
{
    private const float DamagedFraction = 0.6f;

    private readonly List<Thing> damagedCores = new();

    public Alert_CerebrexCoreDamaged()
    {
        defaultLabel = "CerebrexFlavourPack_Alert_CoreDamaged".Translate();
        defaultExplanation = "CerebrexFlavourPack_Alert_CoreDamagedDesc".Translate();
        defaultPriority = AlertPriority.High;
    }

    public override AlertReport GetReport()
    {
        damagedCores.Clear();
        List<Map> maps = Find.Maps;
        for (int i = 0; i < maps.Count; i++)
        {
            // Shared reused buffer - read what is needed inside this loop and do not retain it.
            List<Building> sharedBuffer = maps[i].listerBuildings
                .AllBuildingsColonistOfDef(CerebrexFlavourPackDefOf.CFP_CerebrexCore);
            for (int j = 0; j < sharedBuffer.Count; j++)
            {
                Building core = sharedBuffer[j];
                if (core.HitPoints < core.MaxHitPoints * DamagedFraction)
                {
                    damagedCores.Add(core);
                }
            }
        }

        return AlertReport.CulpritsAre(damagedCores);
    }
}
