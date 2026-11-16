using System.Linq;
using AnoMech.Core.Game.PartyMit;
using AnoMech.Core.UserActions;
using static AnoMech.Core.Game.PartyMit.MitActionId;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Scenarios.Fru;

// Sheet rows several FRU plans repeat. A tank's rows are written per job: the sheet's tank tabs
// give each job its own cooldowns for a hit, and which jobs sit in the seats is the comp's business.
internal static class FruMitigationRows
{
    // A short mitigation goes out this long before its hit; a long one (15 s or more) goes out a bit
    // earlier so it is still up when a hit's follow-up lands.
    public const float Short = 2f;
    public const float Long = 5f;

    // A tank's cooldowns pressed together for one hit. At is the hit's time; each button goes out
    // its own lead before it (Long for 15 s or more of effect, Short for less).
    public static MitPlanEntry[] Sink(SheetColumn tank, JobId job, float hit, string label, params uint[] buttons)
        => buttons.Select(b => Row(tank, job, b, hit, label)).ToArray();

    public static MitPlanEntry Row(SheetColumn tank, JobId job, uint button, float hit, string label, bool buddy = false)
    {
        var duration = MitCatalogue.Find(button)!.Duration;
        return new(tank, button, hit - (duration >= 15f ? Long : Short), label, [hit], [job], Buddy: buddy);
    }

    // Everything a job has that isn't an invulnerability, for a hit it can't avoid.
    public static MitPlanEntry[] KitchenSink(SheetColumn tank, JobId job, float hit, string label) => job switch
    {
        Warrior => Sink(tank, job, hit, label, MitActionId.Rampart, Damnation, ThrillOfBattle, Bloodwhetting),
        Paladin => Sink(tank, job, hit, label, MitActionId.Rampart, Guardian, Bulwark, HolySheltron),
        Gunbreaker => Sink(tank, job, hit, label, MitActionId.Rampart, Camouflage, GreatNebula, HeartOfCorundum),
        DarkKnight => Sink(tank, job, hit, label, MitActionId.Rampart, ShadowedVigil, DarkMind, TheBlackestNight, Oblation),
        _ => [],
    };

    // What a tank gives the other one for a hit, by the giver's job.
    public static MitPlanEntry[] BuddyMit(SheetColumn giver, JobId job, float hit, string label) => job switch
    {
        Warrior => [Row(giver, job, NascentFlash, hit, label, buddy: true)],
        Paladin => [Row(giver, job, Intervention, hit, label, buddy: true)],
        Gunbreaker => [Row(giver, job, HeartOfCorundum, hit, label, buddy: true)],
        DarkKnight => [Row(giver, job, TheBlackestNight, hit, label, buddy: true), Row(giver, job, Oblation, hit, label, buddy: true)],
        _ => [],
    };

    public static MitPlanEntry[] KitchenSinks(SheetColumn tank, float hit, string label)
        => TankJobs.SelectMany(job => KitchenSink(tank, job, hit, label)).ToArray();

    public static MitPlanEntry[] BuddyMits(SheetColumn giver, float hit, string label)
        => TankJobs.SelectMany(job => BuddyMit(giver, job, hit, label)).ToArray();

    // Each tank job's invulnerability, in either seat.
    // Only the tank who takes the hit presses its own: the plan's Baiter.
    public static MitPlanEntry[] Invulns(SheetColumn tank, float hit, string label) =>
    [
        Row(tank, Warrior, Holmgang, hit, label) with { ForBaiter = true },
        Row(tank, Paladin, HallowedGround, hit, label) with { ForBaiter = true },
        Row(tank, Gunbreaker, Superbolide, hit, label) with { ForBaiter = true },
        Row(tank, DarkKnight, LivingDead, hit, label) with { ForBaiter = true },
    ];

    // The sheet's "Hallowed Wing" cooldowns, in either seat.
    public static MitPlanEntry[] HallowedWing(SheetColumn tank, float hit, string label) =>
    [
        Row(tank, Warrior, Damnation, hit, label), Row(tank, Warrior, ThrillOfBattle, hit, label),
        Row(tank, Paladin, Guardian, hit, label),
        Row(tank, Gunbreaker, GreatNebula, hit, label),
        Row(tank, DarkKnight, ShadowedVigil, hit, label), Row(tank, DarkKnight, DarkMind, hit, label),
    ];

    // The main tank soloing an Akh Morn while the other seven stack on the other tank: the sheet's
    // "7/1" cooldowns for the lighter buttons, and the other tank's buddy mitigation.
    public static MitPlanEntry[] SoloAkhMorn(float hit, string label) =>
    [
        Row(MT, Warrior, Bloodwhetting, hit, label), Row(MT, Gunbreaker, HeartOfCorundum, hit, label),
        Row(MT, Paladin, Bulwark, hit, label), Row(MT, Paladin, HolySheltron, hit, label),
        Row(MT, DarkKnight, Oblation, hit, label), Row(MT, DarkKnight, TheBlackestNight, hit, label),
        .. BuddyMits(OT, hit, label),
    ];

    // The sheet's "Barrier" for the Extras column: a Red Mage's Magick Barrier, a Pictomancer's Tempera.
    public static MitPlanEntry[] Barrier(float hit, string label) =>
    [
        new(Extras, MagickBarrier, hit - Short, label, [hit], [RedMage]),
        new(Extras, TemperaCoat, hit - Short - 1f, label, [hit], [Pictomancer]),
        new(Extras, TemperaGrassa, hit - Short, label, [hit], [Pictomancer]),
    ];

    public static MitPlanEntry Dismantled(float hit, string label) => new(Extras, Dismantle, hit - Short, label, [hit], [Machinist]);

    public static readonly JobId[] TankJobs = [Warrior, Paladin, Gunbreaker, DarkKnight];
}
