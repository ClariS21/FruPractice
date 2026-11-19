using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.PartyMit;
using static AnoMech.Core.Game.PartyMit.SheetColumn;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class MitPlanChainTests
{
    private static readonly MitProfile Profile = new(325200f, 226600f, 205200f, 0.61f, 1f, 0.92f, 0.69f, 3f);

    private static MitPlanData Plan(float[] hits, params MitPlanEntry[] rows)
        => new(Profile, [new MitHitDef("hit", "Hit", 1000f, null)], hits.Select(t => new MitScheduledHit(t, ["hit"])).ToArray(), rows);

    [Test]
    public void EachSliceLandsOnTheRunsClockWithItsOwnKeys()
    {
        var chained = MitPlanChain.Of(
            (Plan([10f], new MitPlanEntry(WHM, MitActionId.Temperance, 5f, "a", [10f])), 0f),
            (Plan([10f], new MitPlanEntry(MT, MitActionId.Reprisal, 5f, "b", [10f])), 100f));

        Assert.That(chained.Hits.Select(h => h.Key), Is.EqualTo(new[] { "0:hit", "1:hit" }));
        Assert.That(chained.Schedule.Select(s => (s.At, s.Keys.Single())), Is.EqualTo(new[] { (10f, "0:hit"), (110f, "1:hit") }));
        Assert.That(chained.Entries.Select(e => (e.At, e.Covers.Single())), Is.EqualTo(new[] { (5f, 10f), (105f, 110f) }));
    }

    [Test]
    public void ALaterSlicesPreStartRowIsLeftToTheSliceBefore()
    {
        var chained = MitPlanChain.Of(
            (Plan([10f]), 0f),
            (Plan([10f], new MitPlanEntry(WHM, MitActionId.Temperance, -2f, "pre", [10f])), 50f));

        Assert.That(chained.Entries, Is.Empty);
    }

    // Reprisal's recast is 60s.
    [Test]
    public void AButtonStillOnRecastGoesOutWhenItsBackOrNotAtAll()
    {
        var chained = MitPlanChain.Of(
            (Plan([10f], new MitPlanEntry(MT, MitActionId.Reprisal, 5f, "first", [10f])), 0f),
            (Plan([10f, 30f], new MitPlanEntry(MT, MitActionId.Reprisal, 0f, "delayed", [10f, 30f])), 50f),
            (Plan([10f], new MitPlanEntry(MT, MitActionId.Reprisal, 0f, "lost", [10f])), 100f));

        Assert.That(chained.Entries.Select(e => (e.Label, e.At, e.Covers.Length)),
            Is.EqualTo(new[] { ("first", 5f, 1), ("delayed", 65f, 1) }));
    }

    // One button on one column for two jobs, or for two partners: no comp has one seat press both,
    // so neither holds the other's recast.
    [Test]
    public void RowsNoSeatCanPressBothDontHoldEachOthersRecast()
    {
        var byJob = MitPlanChain.Of((Plan([10f],
            new MitPlanEntry(MT, MitActionId.Rampart, 5f, "warrior", [10f], [Warrior]),
            new MitPlanEntry(MT, MitActionId.Rampart, 5f, "paladin", [10f], [Paladin])), 0f));
        var byPartner = MitPlanChain.Of((Plan([10f],
            new MitPlanEntry(MT, MitActionId.Rampart, 5f, "with a warrior", [10f], Partners: [Warrior]),
            new MitPlanEntry(MT, MitActionId.Rampart, 5f, "with a paladin", [10f], Partners: [Paladin])), 0f));

        Assert.That(byJob.Entries.Select(e => e.Label), Is.EqualTo(new[] { "warrior", "paladin" }));
        Assert.That(byPartner.Entries.Select(e => e.Label), Is.EqualTo(new[] { "with a warrior", "with a paladin" }));
    }

    // Filters that overlap leave a seat that could press both, and a row aimed at another seat or kept
    // to the baiter is still its presser's button.
    [Test]
    public void RowsOneSeatCouldPressBothStillShareTheRecast()
    {
        var chained = MitPlanChain.Of((Plan([10f, 30f],
            new MitPlanEntry(MT, MitActionId.Rampart, 5f, "either", [10f], [Warrior, Paladin]),
            new MitPlanEntry(MT, MitActionId.Rampart, 6f, "paladin", [10f], [Paladin]),
            new MitPlanEntry(WHM, MitActionId.Aquaveil, 5f, "on the main tank", [10f], Target: PartyRole.MainTank),
            new MitPlanEntry(WHM, MitActionId.Aquaveil, 6f, "on the off tank", [10f], Target: PartyRole.OffTank),
            new MitPlanEntry(OT, MitActionId.Rampart, 5f, "any", [10f]),
            new MitPlanEntry(OT, MitActionId.Rampart, 6f, "the baiter", [10f], ForBaiter: true)), 0f));

        Assert.That(chained.Entries.Select(e => e.Label), Is.EqualTo(new[] { "either", "on the main tank", "any" }));
    }
}
