using AnoMech.Core.UserActions;

namespace AnoMech.Scenarios.Fru.P5ParadiseRegained;

// The sheet gives Wings Dark and Light once per cast: one tank invulnerabilities it, the other
// kitchen-sinks it. Of a pair, the first of Warrior, Paladin, Gunbreaker, Dark Knight invulns the
// first cast and the other the second; a same-job pair has the main tank do the first and the off
// tank the second. Nobody changes lanes: the main tank takes the cleave then the buster, the off
// tank the buster then the cleave.
internal static class FruP5WingsTankPair
{
    private static readonly JobId[] Order = [JobId.Warrior, JobId.Paladin, JobId.Gunbreaker, JobId.DarkKnight];

    // Whether the seat's tank invulnerabilities the cast (round 0 is the first, 1 the second).
    public static bool Invulns(bool mainTank, JobId job, JobId partner, int round)
    {
        var first = job == partner ? mainTank : System.Array.IndexOf(Order, job) < System.Array.IndexOf(Order, partner);
        return round == 0 ? first : !first;
    }
}
