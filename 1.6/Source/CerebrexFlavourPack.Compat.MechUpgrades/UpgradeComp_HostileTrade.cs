using System.Collections.Generic;
using MU;
using Verse;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

public class UpgradeComp_HostileTrade : UpgradeComp
{
    public UpgradeCompProperties_HostileTrade Props => (UpgradeCompProperties_HostileTrade)props;

    public override IEnumerable<string> ExtraDescStrings()
    {
        yield return "CFP_HostileTrade_Desc".Translate();

        if (Props.ignorePermanentEnemy)
        {
            yield return "CFP_HostileTrade_DescPermanentEnemy".Translate();
        }
    }
}
