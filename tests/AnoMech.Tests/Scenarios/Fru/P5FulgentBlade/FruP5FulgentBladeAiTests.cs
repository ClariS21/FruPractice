using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Scenarios;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru.P5FulgentBlade;

namespace AnoMech.Tests;

public class FruP5FulgentBladeAiTests
{
    [Test]
    public void BotsGatherAndLeaveTheFirstStackAtDifferentTimes()
    {
        var departures = new Dictionary<PartyRole, float>();
        var firstStack = new Dictionary<PartyRole, Vector3>();
        var largestHoldingSpread = 0f;
        var largestMovingSpread = 0f;
        var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 22.55f,
            Probe = probe =>
            {
                var positions = Enum.GetValues<PartyRole>()
                    .Select(role => (Role: role, Position: probe.Member(role)?.Position))
                    .Where(item => item.Position.HasValue)
                    .Select(item => (item.Role, Position: item.Position!.Value))
                    .ToArray();
                if (positions.Length != 8) return;
                var center = positions.Aggregate(Vector3.Zero, (sum, item) => sum + item.Position) / positions.Length;
                var spread = positions.Average(item => Vector3.Distance(item.Position, center));
                if (probe.Time is >= 18f and < 20.65f) largestHoldingSpread = MathF.Max(largestHoldingSpread, (float)spread);
                if (probe.Time is >= 20.7f and < 22.5f) largestMovingSpread = MathF.Max(largestMovingSpread, (float)spread);
                if (probe.Crossed(20.65f))
                    foreach (var (role, position) in positions) firstStack[role] = position;
                if (probe.Time < 20.66f) return;
                foreach (var (role, position) in positions)
                    if (!departures.ContainsKey(role) && firstStack.TryGetValue(role, out var start)
                        && Vector3.Distance(position, start) > 0.08f)
                        departures[role] = probe.Time;
            },
        });

        Assert.That(run.Passed, run.ToString);
        Assert.That(largestHoldingSpread, Is.LessThanOrEqualTo(1.2f));
        Assert.That(largestMovingSpread, Is.LessThanOrEqualTo(3f));
        Assert.That(departures, Has.Count.EqualTo(8));
        // Still staggered, but every bot must be 0.8 s clear of the next wave's lines, which narrows the spread.
        Assert.That(departures.Values.Max() - departures.Values.Min(), Is.GreaterThanOrEqualTo(0.1f));
    }

    [Test]
    public void TwentyDrillSeedsClearTheExalines()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, seed,
                new ScenarioRunOptions { StopAt = 31.9f, WriteArtifactsOnFailure = false });
            Assert.That(run.Passed, Is.True, run.ToString());
        }
    }

    private static void Force(object overrides, int layout)
    {
        var o = (FruP5FulgentBladeStateOverrides)overrides;
        o.PositionIndex = layout % 4;
        o.RotationIndex = layout / 4 % 4;
        o.Mirrored = layout / 16 == 1;
    }

    [Test]
    public void FiftySeedsAcrossEveryLayoutDodgeEveryLine()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var layout = seed % 32;
            var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, seed,
                new ScenarioRunOptions { StopAt = 33.5f, WriteArtifactsOnFailure = false, Overrides = o => Force(o, layout) });
            Assert.That(run.Passed, $"seed {seed} layout {layout}: {run}");
        }
    }

    [Test]
    public void EveryLayoutHasARoomyStratSpotForEachPairOfWaves()
    {
        for (var layout = 0; layout < 32; layout++)
        {
            var overrides = new FruP5FulgentBladeStateOverrides();
            Force(overrides, layout);
            var state = new FruP5FulgentBladeState(new Rng(layout), overrides);
            var spots = FruP5FulgentBladeAi.PlanSpots(state);
            for (var spot = 0; spot < 4; spot++)
            {
                var waves = spot == 0 ? new[] { 0, 1 } : new[] { 2 * spot, 2 * spot + 1 };
                Assert.That(FruP5FulgentBladeAi.Margin(state, spots[spot], waves), Is.GreaterThanOrEqualTo(1f), $"layout {layout} spot {spot}");
                Assert.That(Vector3.Distance(spots[spot], state.Origin), Is.LessThan(7f), $"layout {layout} spot {spot}");
            }
        }
    }

    [Test]
    public void PartyHoldsTheStratSpotsAndStepsRightAfterTheFreeingWave()
    {
        foreach (var layout in new[] { 0, 5, 10, 17, 22, 27, 31 })
        {
            var overrides = new FruP5FulgentBladeStateOverrides();
            Force(overrides, layout);
            var spots = FruP5FulgentBladeAi.PlanSpots(new FruP5FulgentBladeState(new Rng(1), overrides));
            var centroidAtArrival = new Dictionary<int, float>();
            var departures = new Dictionary<(int Step, PartyRole Role), float>();
            var holding = new Dictionary<PartyRole, Vector3>();
            var run = ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, 1, new ScenarioRunOptions
            {
                StopAt = 31.5f,
                WriteArtifactsOnFailure = false,
                Overrides = o => Force(o, layout),
                Probe = probe =>
                {
                    var members = Enum.GetValues<PartyRole>().Select(r => (Role: r, Member: probe.Member(r))).Where(m => m.Member is not null).ToArray();
                    if (members.Length != 8) return;
                    for (var step = 1; step <= 3; step++)
                    {
                        var freeing = FruP5FulgentBladeAi.LastHitOfWave(2 * step - 1);
                        var nextWave = FruP5FulgentBladeAi.FirstHitOfWave(2 * step);
                        if (probe.Crossed(freeing))
                            foreach (var (role, member) in members) holding[role] = member!.Position;
                        if (probe.Time > freeing && probe.Time < nextWave)
                            foreach (var (role, member) in members)
                                if (holding.TryGetValue(role, out var from) && !departures.ContainsKey((step, role))
                                    && Vector3.Distance(from, member!.Position) > 0.05f)
                                    departures[(step, role)] = probe.Time - freeing;
                        if (probe.Crossed(nextWave - 0.05f))
                        {
                            var centroid = members.Aggregate(Vector3.Zero, (sum, m) => sum + m.Member!.Position) / 8f;
                            centroidAtArrival[step] = Vector3.Distance(centroid, spots[step]);
                        }
                    }
                },
            });
            Assert.That(run.Passed, run.ToString);
            Assert.That(centroidAtArrival, Has.Count.EqualTo(3));
            foreach (var (step, distance) in centroidAtArrival)
                Assert.That(distance, Is.LessThanOrEqualTo(1.5f), $"layout {layout} step {step}");
            Assert.That(departures, Has.Count.EqualTo(24), $"layout {layout}");
            foreach (var (key, delay) in departures)
                Assert.That(delay, Is.InRange(0f, 1.2f), $"layout {layout} {key}");
        }
    }

    // Per bot and step over every layout: how long before the next wave's first hit the bot is out of
    // every band that wave hits, and how long before it the bot has reached its new spot.
    private static List<(PartyRole Role, int Step, float Clear, float Arrived, bool SpotInBand)> MeasureMargins(int seeds)
    {
        var results = new List<(PartyRole, int, float, float, bool)>();
        for (var seed = 0; seed < seeds; seed++)
        {
            var layout = seed % 32;
            var overrides = new FruP5FulgentBladeStateOverrides();
            Force(overrides, layout);
            var state = new FruP5FulgentBladeState(new Rng(seed), overrides);
            var lastInside = new Dictionary<(int, PartyRole), float>();
            var lastMoving = new Dictionary<(int, PartyRole), float>();
            var previous = new Dictionary<PartyRole, Vector3>();
            var final = new Dictionary<(int, PartyRole), Vector3>();
            ScenarioRun.Execute(typeof(FruP5FulgentBladeScenario), 0, seed, new ScenarioRunOptions
            {
                StopAt = 31f,
                WriteArtifactsOnFailure = false,
                Overrides = o => Force(o, layout),
                Probe = probe =>
                {
                    foreach (var role in Enum.GetValues<PartyRole>())
                    {
                        if (probe.Member(role) is not { } member) continue;
                        var position = member.Position;
                        for (var step = 1; step <= 3; step++)
                        {
                            var freeing = FruP5FulgentBladeAi.LastHitOfWave(2 * step - 1);
                            var next = FruP5FulgentBladeAi.FirstHitOfWave(2 * step);
                            if (probe.Time <= freeing || probe.Time > next) continue;
                            if (FruP5FulgentBladeAi.Margin(state, position, [2 * step]) < 0f) lastInside[(step, role)] = probe.Time;
                            if (previous.TryGetValue(role, out var before) && Vector3.Distance(before, position) > 0.002f) lastMoving[(step, role)] = probe.Time;
                            final[(step, role)] = position;
                        }
                        previous[role] = position;
                    }
                },
            });
            foreach (var role in Enum.GetValues<PartyRole>())
                for (var step = 1; step <= 3; step++)
                {
                    var next = FruP5FulgentBladeAi.FirstHitOfWave(2 * step);
                    var freeing = FruP5FulgentBladeAi.LastHitOfWave(2 * step - 1);
                    var clear = next - lastInside.GetValueOrDefault((step, role), freeing);
                    var arrived = next - lastMoving.GetValueOrDefault((step, role), freeing);
                    results.Add((role, step, clear, arrived, FruP5FulgentBladeAi.Margin(state, final[(step, role)], [2 * step]) < 0f));
                }
        }
        return results;
    }

    [Test]
    public void EveryBotIsOutOfTheNextWavesLinesAtLeastEightTenthsOfASecondEarly()
    {
        var results = MeasureMargins(50);
        foreach (var group in results.GroupBy(r => r.Role))
        {
            var clear = group.Select(r => r.Clear).OrderBy(c => c).ToArray();
            var arrived = group.Select(r => r.Arrived).OrderBy(c => c).ToArray();
            TestContext.Out.WriteLine($"{group.Key,-14} clear min {clear[0]:F2} median {clear[clear.Length / 2]:F2} | arrived min {arrived[0]:F2} median {arrived[arrived.Length / 2]:F2}");
        }
        var late = results.Where(r => r.Arrived < 0.8f).ToArray();
        TestContext.Out.WriteLine($"arrived under 0.8 s early: {late.Length} of {results.Count}; of those, spot inside the next wave's lines: {late.Count(r => r.SpotInBand)}");
        Assert.That(late.Count(r => r.SpotInBand), Is.Zero);
        Assert.That(results.Min(r => r.Clear), Is.GreaterThanOrEqualTo(0.8f));
    }

    // The user's first FFLogs clear, in its own frame (x east, y south, yalms from the centre): where
    // its three line groups cross, then the spots they stood on, one per pair of waves.
    private static readonly Vector2[] UserIntersections = [new(-3f, 4.18f), new(11.32f, 10f), new(17f, -4.32f)];
    private static readonly Vector2[] UserSpots = [new(8.5f, -1.4f), new(3.3f, -1.4f), new(6.2f, 3.3f), new(9.1f, 0.2f)];

    [Test]
    public void StratSpotsMatchTheUsersOwnClear()
    {
        var corners = new List<(Func<Vector2, Vector2> Map, Vector3[] Spots)>();
        for (var layout = 0; layout < 32; layout++)
        {
            var overrides = new FruP5FulgentBladeStateOverrides();
            Force(overrides, layout);
            var state = new FruP5FulgentBladeState(new Rng(0), overrides);
            var crossings = Enumerable.Range(0, 3).Select(g => Cross(state, g)).ToArray();
            // The user's centre is the sim's: only a mirror and a quarter turn separate the layouts.
            foreach (var mirror in new[] { false, true })
                for (var turn = 0; turn < 4; turn++)
                {
                    var t = turn;
                    var m = mirror;
                    Vector2 Map(Vector2 p)
                    {
                        var q = m ? new Vector2(-p.X, p.Y) : p;
                        var (sin, cos) = (MathF.Sin(t * MathF.PI / 2f), MathF.Cos(t * MathF.PI / 2f));
                        return new Vector2(q.X * cos - q.Y * sin, q.X * sin + q.Y * cos);
                    }
                    if (UserIntersections.Select(Map).Zip(crossings).All(pair => Vector2.Distance(pair.First, pair.Second) < 0.6f))
                        corners.Add((Map, FruP5FulgentBladeAi.PlanSpots(state)));
                }
        }
        Assert.That(corners, Is.Not.Empty, "no layout lays the user's clear over the sim's");
        foreach (var (map, spots) in corners)
            for (var i = 0; i < 4; i++)
                Assert.That(Vector2.Distance(map(UserSpots[i]), new Vector2(spots[i].X, spots[i].Z)), Is.LessThanOrEqualTo(1.6f), $"spot {i}");
    }

    // Where the two seams of a group cross.
    private static Vector2 Cross(FruP5FulgentBladeState state, int group)
    {
        var (a, b) = (state.Seam(group, 0), state.Seam(group, 1));
        var (na, nb) = (new Vector2(MathF.Sin(a.Rotation), MathF.Cos(a.Rotation)), new Vector2(MathF.Sin(b.Rotation), MathF.Cos(b.Rotation)));
        var (ca, cb) = (Vector2.Dot(new Vector2(a.Position.X, a.Position.Z), na), Vector2.Dot(new Vector2(b.Position.X, b.Position.Z), nb));
        var det = na.X * nb.Y - na.Y * nb.X;
        return new Vector2((ca * nb.Y - cb * na.Y) / det, (na.X * cb - nb.X * ca) / det);
    }
}
