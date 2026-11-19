using System.Collections.Generic;
using System.Linq;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Scenarios.Umad;

// Sheet rows the DMU plans share. The sheet's tank cells name buttons by kind ("Rampart + 90s +
// Short Mit"); each tank job has its own of each, so a tank row is written once per job and the
// job in the seat picks it. The tank columns follow the boss or the role a tank has, not its seat:
// a plan is built for the run's pair, so each column is the seat that holds it.
internal static class UmadMitigationRows
{
    // As FRU's: a short mitigation goes out this long before its hit, a 15 s or longer one earlier.
    public const float Short = 2f;
    public const float Long = 5f;

    public static readonly JobId[] TankJobs = [Warrior, Paladin, Gunbreaker, DarkKnight];

    public static SheetColumn Column(PartyRole seat) => seat == PartyRole.OffTank ? OT : MT;

    public static float Lead(uint button) => MitCatalogue.Find(button)!.Duration >= 15f ? Long : Short;

    // The tank cells' kinds of button, per job.
    public static uint FortyPercent(JobId job) => job switch
    {
        Warrior => Damnation,
        Paladin => Guardian,
        Gunbreaker => GreatNebula,
        _ => ShadowedVigil,
    };

    public static uint NinetySeconds(JobId job) => job switch
    {
        Warrior => ThrillOfBattle,
        Paladin => Bulwark,
        Gunbreaker => Camouflage,
        _ => DarkMind,
    };

    public static uint ShortMit(JobId job) => job switch
    {
        Warrior => Bloodwhetting,
        Paladin => HolySheltron,
        Gunbreaker => HeartOfCorundum,
        _ => TheBlackestNight,
    };

    public static uint Invulnerability(JobId job) => job switch
    {
        Warrior => Holmgang,
        Paladin => HallowedGround,
        Gunbreaker => Superbolide,
        _ => LivingDead,
    };

    // One button of a kind for each job, pressed its own lead before `hit` (or at `at`).
    public static IEnumerable<MitPlanEntry> PerJob(SheetColumn tank, System.Func<JobId, uint> kind, float hit, string label, float[] covers,
        float? at = null, JobId[]? jobs = null)
        => (jobs ?? TankJobs).Select(job => new MitPlanEntry(tank, kind(job), at ?? hit - Lead(kind(job)), label, covers, [job]));

    // Every tank has a Rampart, so one row stands for all four.
    public static MitPlanEntry Rampart(SheetColumn tank, float hit, string label, float[] covers, float? at = null)
        => new(tank, MitActionId.Rampart, at ?? hit - Long, label, covers);

    // The sheet's "Rampart + 90s + Short Mit".
    public static IEnumerable<MitPlanEntry> RampartNinetyShort(SheetColumn tank, float hit, string label, float[] covers)
        => [Rampart(tank, hit, label, covers), .. PerJob(tank, NinetySeconds, hit, label, covers), .. PerJob(tank, ShortMit, hit, label, covers)];

    // The sheet's "40% + Short Mit".
    public static IEnumerable<MitPlanEntry> FortyShort(SheetColumn tank, float hit, string label, float[] covers)
        => [.. PerJob(tank, FortyPercent, hit, label, covers), .. PerJob(tank, ShortMit, hit, label, covers)];

    // The extras column's barrier: a Red Mage's Magick Barrier, a Pictomancer's Tempera.
    public static MitPlanEntry[] Barrier(float at, string label, float[] covers) =>
    [
        new(Extras, MagickBarrier, at, label, covers, [RedMage]),
        new(Extras, TemperaCoat, at - 1f, label + " (Tempera Grassa next)", [], [Pictomancer]),
        new(Extras, TemperaGrassa, at, label, covers, [Pictomancer]),
    ];

    public static MitPlanEntry Dismantled(float at, string label, float[] covers, MitSource? on = null)
        => new(Extras, Dismantle, at, label, covers, [Machinist], On: on);

    // The sheet's tick in the extras column: every extra a comp brings.
    public static MitPlanEntry[] Extra(float at, string label, float[] covers, MitSource? on = null)
        => [Dismantled(at, label, covers, on), .. Barrier(at, label, covers)];
}
