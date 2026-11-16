using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;

namespace AnoMech.Scenarios.Fru.P4DarklitDragonsong;

internal static class FruP4TankPair
{
    internal sealed record Tab(string Name, JobId First, JobId Second)
    {
        public int ColumnFor(PartyRole seat, JobId mainTank)
            => mainTank == First ? seat == PartyRole.MainTank ? 3 : 6
                : seat == PartyRole.MainTank ? 10 : 13;
    }

    private static readonly Tab[] Tabs =
    [
        new("WARPLD", JobId.Warrior, JobId.Paladin),
        new("WARGNB", JobId.Warrior, JobId.Gunbreaker),
        new("GNBPLD", JobId.Gunbreaker, JobId.Paladin),
        new("GNBDRK", JobId.Gunbreaker, JobId.DarkKnight),
        new("WARDRK", JobId.Warrior, JobId.DarkKnight),
        new("PLDDRK", JobId.Paladin, JobId.DarkKnight),
    ];

    internal static Tab? For(JobId mainTank, JobId offTank)
    {
        foreach (var tab in Tabs)
            if (mainTank == tab.First && offTank == tab.Second || mainTank == tab.Second && offTank == tab.First)
                return tab;
        return null;
    }

    public static PartyRole SomberBaiter(JobId mainTank, JobId offTank)
    {
        var tab = For(mainTank, offTank);
        if (tab is null) return PartyRole.MainTank;
        if (tab.Name is "WARGNB" or "WARDRK") return PartyRole.MainTank;
        var baitJob = tab.Name switch
        {
            "WARPLD" => JobId.Warrior,
            "GNBPLD" => JobId.Gunbreaker,
            _ => JobId.DarkKnight,
        };
        return mainTank == baitJob ? PartyRole.MainTank : PartyRole.OffTank;
    }
}
