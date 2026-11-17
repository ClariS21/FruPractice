using System.Numerics;
using System.Text.Json;
using AnoMech.Core.Game.Guidance;
using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

public class PathDebriefTests
{
    [Test]
    public void ClipInterpolatesAtSnapshotAndIgnoresUnrecordedTimes()
    {
        (float, Vector2)[] path = [(0, new(0, 0)), (2, new(12, 0))];
        var shape = new GuidanceShape.Circle(new(6, 0), 1);
        var findings = PathDebrief.Analyze(path, [], [new(shape, 1, "light"), new(shape, 3, "future")]);
        Assert.That(findings.OfType<PathFinding.Clip>().Select(c => (c.HazardLabel, c.Time)), Is.EqualTo(new[] { ("light", 1f) }));
    }

    [Test]
    public void LateDepartureAndWideArrivalHaveMeasuredValues()
    {
        (float, Vector2)[] path = [(0, Vector2.Zero), (5, Vector2.Zero), (6, new(4, 0))];
        var findings = PathDebrief.Analyze(path, [GuidanceLogicTests.Spot(12, 0, 0, 6)], []);
        Assert.That(findings.OfType<PathFinding.Late>().Single().SecondsLate, Is.EqualTo(1.8f).Within(0.001f));
        Assert.That(findings.OfType<PathFinding.Wide>().Single().Distance, Is.EqualTo(8));
    }

    [Test]
    public void OnTimeTightPathAndUnfinishedLegHaveNoFindings()
    {
        (float, Vector2)[] path = [(0, Vector2.Zero), (2, Vector2.Zero), (4, new(12, 0)), (6, new(12, 0))];
        Assert.That(PathDebrief.Analyze(path, [GuidanceLogicTests.Spot(12, 0, 0, 6), GuidanceLogicTests.Spot(30, 0, 5, 10)], []), Is.Empty);
    }

    [Test]
    public void SnapshotColorsDistinguishClipNearMissAndSafe()
    {
        (float, Vector2)[] path = [(0, Vector2.Zero), (1, Vector2.Zero), (2, Vector2.Zero)];
        Assert.That(PathDebrief.Colors(path, [new(new GuidanceShape.Circle(Vector2.Zero, 1), 1, "light")]), Is.EqualTo(new[] { PathSafety.Safe, PathSafety.Clip, PathSafety.Safe }));
        Assert.That(PathDebrief.Colors(path, [new(new GuidanceShape.Circle(new(1.5f, 0), 1), 1, "light")])[1], Is.EqualTo(PathSafety.Near));
    }

    [Test]
    public void RealApocalypsePathClipsNearObservedHitAndIsWide()
    {
        FakeGame.Install();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures/apocalypse-real-path.json")));
        var root = json.RootElement;
        var path = root.GetProperty("path").EnumerateArray().Select(p => (p[0].GetSingle(), new Vector2(p[1].GetSingle(), p[2].GetSingle()))).ToArray();
        // These are instant light casts: the sim snapshots at cast + 0.00s and displays damage
        // 0.57s later. FFLogs' 35.8s hit is visual damage, so expect the snapshot near 35.17s.
        // The sparse path also predicts a clip at 41.20s, absent from the hit list. The fixture
        // has no continuous movement or eligibility/status history to resolve that discrepancy;
        // this replay validates the observed clip, not exact parity with every logged hit.
        var hazards = root.GetProperty("lightCasts").EnumerateArray().Select(p =>
            new GuidanceHazard(new GuidanceShape.Circle(new(p[1].GetSingle(), p[2].GetSingle()), ApocalypseHazards.Radius),
                p[0].GetSingle() + ApocalypseHazards.SnapshotOffset, "Apocalypse")).ToArray();
        Assert.That(ApocalypseHazards.SnapshotOffset, Is.Zero);
        var findings = PathDebrief.Analyze(path, [GuidanceLogicTests.Spot(10, -4, 25.27f, 34.59f)], hazards);
        var observed = root.GetProperty("lightHitOnThisPlayerAt")[0].GetSingle();
        var clips = findings.OfType<PathFinding.Clip>().ToArray();
        TestContext.Out.WriteLine($"Radius {ApocalypseHazards.Radius}; clips: {string.Join(", ", clips.Select(c => c.Time.ToString("F2")))}; observed hit {observed:F2}");
        Assert.That(clips.Any(c => MathF.Abs(c.Time - observed) <= 0.7f), Is.True);
        Assert.That(findings.OfType<PathFinding.Wide>(), Is.Not.Empty);
    }
}
