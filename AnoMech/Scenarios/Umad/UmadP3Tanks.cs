using System;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Umad.P3BlackHole;

namespace AnoMech.Scenarios.Umad;

// Which tank seat holds which P3 boss. Of a pair, Chaos goes to the first of Warrior, Dark Knight,
// Gunbreaker, Paladin, Exdeath to the other; a same-job pair keeps Chaos on the main tank.
public sealed record UmadP3Tanks(PartyRole Chaos, PartyRole Exdeath)
{
    public static readonly UmadP3Tanks MainOnChaos = new(PartyRole.MainTank, PartyRole.OffTank);

    private static readonly JobId[] ChaosPriority = [JobId.Warrior, JobId.DarkKnight, JobId.Gunbreaker, JobId.Paladin];

    public static UmadP3Tanks For(JobId mainTank, JobId offTank)
    {
        var main = Rank(mainTank);
        var off = Rank(offTank);
        return off < main ? new(PartyRole.OffTank, PartyRole.MainTank) : MainOnChaos;
    }

    public static UmadP3Tanks For(SimParty party) => For(JobOf(party, PartyRole.MainTank), JobOf(party, PartyRole.OffTank));

    private static JobId JobOf(SimParty party, PartyRole seat) => (JobId)((party.Get(seat) as ISimPartyMember)?.ClassJob ?? 0);

    // A job off the list ranks last, so a pair of unknowns keeps the main tank on Chaos.
    private static int Rank(JobId job) => Array.IndexOf(ChaosPriority, job) is var i and >= 0 ? i : ChaosPriority.Length;

    public ThunderIIIAssignment Seats(UmadP3ThunderPlan plan) => plan switch
    {
        UmadP3ThunderPlan.ChaosTankInvulns => Chaos == PartyRole.MainTank ? ThunderIIIAssignment.MtInvulnsBoth : ThunderIIIAssignment.OtInvulnsBoth,
        UmadP3ThunderPlan.ExdeathTankInvulns => Exdeath == PartyRole.MainTank ? ThunderIIIAssignment.MtInvulnsBoth : ThunderIIIAssignment.OtInvulnsBoth,
        UmadP3ThunderPlan.ShareChaosTankFirst => Chaos == PartyRole.MainTank ? ThunderIIIAssignment.ShareMtFirst : ThunderIIIAssignment.ShareOtFirst,
        UmadP3ThunderPlan.ShareExdeathTankFirst => Exdeath == PartyRole.MainTank ? ThunderIIIAssignment.ShareMtFirst : ThunderIIIAssignment.ShareOtFirst,
        _ => throw new ArgumentOutOfRangeException(nameof(plan), plan, null),
    };
}

// One Thunder III set's two hits, by the boss each tank holds rather than by seat.
public enum UmadP3ThunderPlan
{
    ChaosTankInvulns,
    ExdeathTankInvulns,
    ShareChaosTankFirst,
    ShareExdeathTankFirst,
}
