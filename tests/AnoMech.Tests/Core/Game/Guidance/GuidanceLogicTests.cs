using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;

namespace AnoMech.Tests;

public class GuidanceLogicTests
{
    internal static StratTarget Spot(float x, float z, float issued, float deadline, PartyRole role = PartyRole.CasterDps)
        => new(role, new(x, 0, z), issued, deadline, null, null, null);

    [Test]
    public void SelectsUpcomingIssuedSpotsAndComputesGoTime()
    {
        var trail = new StratTrail(new EventScheduler());
        foreach (var s in new[] { Spot(0, 0, 0, 1), Spot(12, 0, 1, 10), Spot(18, 0, 1, 15), Spot(1, 0, 7, 8), Spot(3, 0, 0, 6, PartyRole.MainTank) }) trail.Record(s);
        var guide = GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 5, trail, []);
        Assert.That(guide.Next!.Deadline, Is.EqualTo(10));
        Assert.That(guide.Following!.Deadline, Is.EqualTo(15));
        Assert.That(guide.GoAt, Is.EqualTo(6.7f).Within(0.0001f));
        Assert.That(guide.ShowGo, Is.False);
        Assert.That(GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 6.8f, trail, []).ShowGo, Is.True);
    }

    [Test]
    public void DangerUsesArrivalAtSnapshotNotJustGeometricIntersection()
    {
        var trail = new StratTrail(new EventScheduler());
        trail.Record(Spot(12, 0, 0, 10));
        var circle = new GuidanceShape.Circle(new(6, 0), 1);
        var guide = GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail, [new(circle, 1, "light")]);
        Assert.That(guide.DangerSegments, Has.Count.EqualTo(1));
        Assert.That(guide.DangerSegments[0].From.X, Is.EqualTo(5).Within(0.001));
        Assert.That(guide.DangerSegments[0].To.X, Is.EqualTo(7).Within(0.001));
        Assert.That(GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail, [new(circle, 3, "light")]).DangerSegments, Is.Empty);
    }

    [Test]
    public void WarnsOnlyInsideAnUpcomingSnapshotAndWorksWithoutSpots()
    {
        var trail = new StratTrail(new EventScheduler());
        var circle = new GuidanceShape.Circle(Vector2.Zero, 1);
        foreach (var (at, danger) in new[] { (-0.1f, false), (0f, true), (1.5f, true), (1.6f, false) })
            Assert.That(GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail, [new(circle, at, "light")]).InDangerNow, Is.EqualTo(danger));
        Assert.That(GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail, []).Next, Is.Null);
    }

    [Test]
    public void FollowingLegUsesTimeAfterReachingFirstSpot()
    {
        var trail = new StratTrail(new EventScheduler());
        trail.Record(Spot(6, 0, 0, 5));
        trail.Record(Spot(6, 6, 0, 8));
        var guide = GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail,
            [new(new GuidanceShape.Circle(new(6, 3), 1), 1.5f, "light")]);
        Assert.That(guide.DangerSegments, Has.Count.EqualTo(1));
        Assert.That(guide.DangerSegments[0].Following, Is.True);
    }

    [Test]
    public void DoesNotPretendToWaitAtFirstSpotWhenWalkingOnToFollowingSpot()
    {
        var trail = new StratTrail(new EventScheduler());
        trail.Record(Spot(6, 0, 0, 5));
        trail.Record(Spot(6, 12, 0, 8));
        var guide = GuidanceLogic.Compute(PartyRole.CasterDps, Vector2.Zero, 0, trail,
            [new(new GuidanceShape.Circle(new(6, 0), 1), 2, "light")]);
        Assert.That(guide.DangerSegments, Is.Empty);
    }
}
