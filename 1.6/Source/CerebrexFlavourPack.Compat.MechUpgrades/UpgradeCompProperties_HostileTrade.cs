using MU;

namespace CerebrexFlavourPack.Compat.MechUpgrades;

public class UpgradeCompProperties_HostileTrade : UpgradeCompProperties
{
    // Also bypass permanentEnemy (savage/cannibal tribes). Pirates still have no trader kinds.
    public bool ignorePermanentEnemy = true;

    public UpgradeCompProperties_HostileTrade()
    {
        compClass = typeof(UpgradeComp_HostileTrade);
    }
}
