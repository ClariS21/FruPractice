using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

// The sheet's tank-pair tabs give Wings Dark and Light per job, whichever seat it sits in: the
// first wing's invulnerability goes to the first of Warrior, Paladin, Gunbreaker, Dark Knight in
// the pair, the second wing's to the other. The invulnerable tank takes the wing's cleave.
internal static class FruP5WingsTankPair
{
    private static readonly JobId[] Order = [JobId.Warrior, JobId.Paladin, JobId.Gunbreaker, JobId.DarkKnight];

    public static bool InvulnsFirst(JobId job, JobId partner)
        => System.Array.IndexOf(Order, job) is var a && a >= 0 && System.Array.IndexOf(Order, job) < (System.Array.IndexOf(Order, partner) is var b && b >= 0 ? b : int.MaxValue);

    // The seat whose job takes the first wing's cleave on its invulnerability.
    public static PartyRole FirstCleaveSeat(JobId mainTank, JobId offTank)
        => InvulnsFirst(offTank, mainTank) && !InvulnsFirst(mainTank, offTank) ? PartyRole.OffTank : PartyRole.MainTank;

    public static PartyRole Other(PartyRole seat) => seat == PartyRole.MainTank ? PartyRole.OffTank : PartyRole.MainTank;
}
