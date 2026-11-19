using System;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;

namespace AnoMech.Scenarios.Umad.P5Full;

// The Omnitank sheet's P5 columns. Of a pair, the first of Warrior, Dark Knight, Gunbreaker, Paladin
// invulns the first Maddening Orchestra's Surprise Holy; the other starts with the boss, so it takes
// the first Surprise Flare, and invulns the second Holy. A same-job pair has the main tank invuln
// first.
internal static class UmadP5TankPair
{
    private static readonly JobId[] FirstInvulnOrder = [JobId.Warrior, JobId.DarkKnight, JobId.Gunbreaker, JobId.Paladin];

    public static bool InvulnsFirst(bool mainTank, JobId job, JobId partner)
        => job == partner ? mainTank : Array.IndexOf(FirstInvulnOrder, job) < Array.IndexOf(FirstInvulnOrder, partner);

    // The seat that takes the given Orchestra's (0 or 1) Surprise Holy.
    public static PartyRole HolyTank(JobId mainTank, JobId offTank, int orchestra)
        => InvulnsFirst(true, mainTank, offTank) == (orchestra == 0) ? PartyRole.MainTank : PartyRole.OffTank;

    public static PartyRole StartsWithBoss(JobId mainTank, JobId offTank) => HolyTank(mainTank, offTank, 1);

    // The run's pair: the seat that invulns first, which the sheet's MT column stands for.
    public static PartyRole FirstInvuln(SimParty party) => HolyTank(JobOf(party, PartyRole.MainTank), JobOf(party, PartyRole.OffTank), 0);

    private static JobId JobOf(SimParty party, PartyRole seat) => (JobId)((party.Get(seat) as ISimPartyMember)?.ClassJob ?? 0);
}
