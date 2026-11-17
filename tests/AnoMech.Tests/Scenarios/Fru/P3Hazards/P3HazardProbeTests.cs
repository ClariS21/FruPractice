using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using static AnoMech.Scenarios.Fru.P3UltimateRelativity.FruP3UltimateRelativityState;

namespace AnoMech.Tests;

// A bot placed 0.5 y inside a reported hazard just before its FiresAt is hit by that very AoE, and one placed
// 0.5 y outside it is not.
public class P3HazardProbeTests
{
    private const PartyRole Player = PartyRole.CasterDps;
    private const PartyRole Tester = PartyRole.PhysRangedDps;

    private delegate GuidanceHazard? Pick(IReadOnlyList<GuidanceHazard> due);

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private sealed record Outcome(bool Found, bool Hit, float SignedDistance, string Detail);

    // Finds the hazard `pick` chooses among those due at `fires`, parks `Tester` 0.5 y inside or outside it
    // (everyone else holds still), and reports whether the matching AoE caught it.
    private static Outcome Probe(Type scenario, int seed, float fires, uint actionId, Pick pick, bool inside, Action<object>? overrides = null)
    {
        GuidanceHazard? hazard = null;
        var none = false;
        Vector2 spot = default;
        var held = new Dictionary<PartyRole, Vector3>();
        bool? hit = null;
        var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
        {
            PlayerRole = Player, WriteArtifactsOnFailure = false, StopAt = fires + 0.2f, Overrides = overrides,
            Probe = p =>
            {
                if (hazard is null && !none && p.Time >= fires - 0.12f)
                {
                    var due = ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(fires - 0.12f, 0.24f)
                        .Where(h => MathF.Abs(h.FiresAt - fires) < 0.03f)
                        // Not the tester's own circle: it is moved to the edge of another one.
                        .Where(h => h.Shape is not GuidanceShape.Circle c || Vector2.Distance(c.Center, Flat(p.Member(Tester)!.Position)) > 1f).ToList();
                    hazard = pick(due);
                    if (hazard is null) { none = true; return; }
                    spot = Place(hazard.Shape, inside ? -0.5f : 0.5f);
                    p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                    {
                        if (resolved.Action.ActionId != actionId || !Matches(hazard.Shape, resolved.Origin.Position)) return;
                        hit = resolved.Hits.Contains(p.Member(Tester)!);
                    });
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (p.Member(role) is { } m) held[role] = m.Position;
                }
                if (hazard is null || p.Time >= fires) return;
                foreach (var (role, position) in held)
                {
                    var member = p.Member(role)!;
                    member.StopMoving();
                    member.SetPosition(role == Tester ? new Vector3(spot.X, 0f, spot.Y) : position);
                }
            },
        });
        if (hazard is null) return new(false, false, 0f, $"no hazard at {fires}: {run}");
        return new(true, hit == true, hazard.Shape.SignedDistance(spot), $"{run}");
    }

    // A point `distance` from the shape's edge (negative: inside).
    private static Vector2 Place(GuidanceShape shape, float distance)
    {
        switch (shape)
        {
            case GuidanceShape.Circle circle:
            {
                var tangent = circle.Center.LengthSquared() > 0.01f ? Vector2.Normalize(new Vector2(-circle.Center.Y, circle.Center.X)) : Vector2.UnitX;
                return circle.Center + tangent * (circle.Radius + distance);
            }
            case GuidanceShape.Area beam:
            {
                var origin = Flat(beam.Query.Source.Position);
                var r = beam.Query.Source.Rotation;
                var forward = new Vector2(MathF.Sin(r), MathF.Cos(r));
                var side = new Vector2(MathF.Cos(r), -MathF.Sin(r));
                return origin + forward * 9f + side * (2.5f + distance);
            }
            default: throw new NotSupportedException(shape.ToString());
        }
    }

    private static bool Matches(GuidanceShape shape, Vector3 origin) => shape switch
    {
        GuidanceShape.Circle circle => Vector2.Distance(Flat(origin), circle.Center) < 0.5f,
        GuidanceShape.Area beam => Vector3.Distance(origin, beam.Query.Source.Position) < 0.1f,
        _ => false,
    };

    private static void AssertEdge(Outcome outside, Outcome inside, string what)
    {
        Assert.That(inside.Found && outside.Found, Is.True, $"{what}: {inside.Detail} / {outside.Detail}");
        Assert.That(inside.SignedDistance, Is.EqualTo(-0.5f).Within(0.05f), what);
        Assert.That(outside.SignedDistance, Is.EqualTo(0.5f).Within(0.05f), what);
        Assert.That(inside.Hit, Is.True, $"{what}: a bot 0.5 y inside was not hit. {inside.Detail}");
        Assert.That(outside.Hit, Is.False, $"{what}: a bot 0.5 y outside was hit. {outside.Detail}");
    }

    private static void AssertBothSides(Type scenario, int seed, float fires, uint action, Pick pick, string what, Action<object>? overrides = null)
        => AssertEdge(Probe(scenario, seed, fires, action, pick, false, overrides), Probe(scenario, seed, fires, action, pick, true, overrides), what);

    private static readonly Pick Fire = hs => hs.FirstOrDefault(h => h.Label == "Dark Fire");

    [Test, Combinatorial]
    public void DarkFireCircleMatchesTheHit([Values(0, 1, 2)] int wave, [Values(1, 2, 3, 5)] int seed)
        => AssertBothSides(typeof(FruP3UltimateRelativityScenario), seed, FireAt(wave), FruConstants.ActionId.DarkFire, Fire, $"fire wave {wave} seed {seed}");

    // Both turning directions and every hourglass position over the seeds; first shot, early and late sweeps.
    [Test, Combinatorial]
    public void SinboundMeltdownLineMatchesTheHit([Values(0, 1, 2)] int wave, [Values(0, 1, 2, 5, 9)] int shot, [Values(0, 1, 2, 3, 5, 7)] int seed)
    {
        var action = shot == 0 ? FruConstants.ActionId.SinboundMeltdownFirst : FruConstants.ActionId.SinboundMeltdownRest;
        Pick pick = hs =>
        {
            var beams = hs.Where(h => h.Label == "Sinbound Meltdown").ToList();
            return beams.Count == 0 ? null : beams[seed % beams.Count];
        };
        AssertBothSides(typeof(FruP3UltimateRelativityScenario), seed, LaserHitAt(wave, shot), action, pick, $"beam wave {wave} shot {shot} seed {seed}");
    }

    [Test, Combinatorial]
    public void DarkEruptionCircleMatchesTheHitInRelativity([Values(1, 2, 3)] int seed)
        => AssertBothSides(typeof(FruP3UltimateRelativityScenario), seed, FinalAt, FruConstants.ActionId.DarkEruption,
            hs => hs.FirstOrDefault(h => h.Label == "Dark Eruption"), $"eruption seed {seed}");

    private static Action<object> Turn(bool clockwise, int rotation) => o =>
    {
        var s = (FruP3ApocalypseStateOverrides)o;
        s.Clockwise = clockwise;
        s.Rotation = rotation;
        s.SpiritTakerTarget = PartyRole.MainTank;
    };

    [Test, Combinatorial]
    public void SpiritTakerCircleMatchesTheHit([Values(1, 2, 3, 4)] int seed, [Values(false, true)] bool clockwise, [Values(0, 2)] int rotation)
        => AssertBothSides(typeof(FruP3ApocalypseScenario), seed, ApocalypseHazards.SpiritTakerAt, FruConstants.ActionId.SpiritTaker,
            hs => hs.FirstOrDefault(h => h.Label == "Spirit Taker"), $"spirit taker seed {seed}", Turn(clockwise, rotation));

    [Test, Combinatorial]
    public void ApocalypseDarkEruptionCircleMatchesTheHit([Values(1, 2, 3)] int seed, [Values(false, true)] bool clockwise)
        => AssertBothSides(typeof(FruP3ApocalypseScenario), seed, ApocalypseHazards.DarkEruptionAt, FruConstants.ActionId.DarkEruption,
            hs => hs.FirstOrDefault(h => h.Label == "Dark Eruption"), $"apocalypse eruption seed {seed}", Turn(clockwise, 1));

    [Test]
    public void FullPhaseForwardsTheSliceThatIsRunning()
    {
        var labels = new HashSet<string>();
        var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, 2, new ScenarioRunOptions
        {
            StopAt = FruP3FullScenario.ApocalypseAt + 45f,
            Probe = p =>
            {
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                foreach (var h in source.Upcoming(p.Time, 1f)) labels.Add(h.Label);
            },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(labels, Is.SupersetOf(new[] { "Dark Fire", "Sinbound Meltdown", "Dark Eruption", "Apocalypse", "Spirit Taker" }));
    }

    // The guidance run asks every frame, and uptime may ask for the whole rest of a run: no horizon, however large,
    // may make a source loop or grow.
    [TestCase(typeof(FruP3UltimateRelativityScenario), 20f)]
    [TestCase(typeof(FruP3ApocalypseScenario), 20f)]
    [TestCase(typeof(FruP3FullScenario), 30f)]
    [TestCase(typeof(FruP3FullScenario), 110f)]
    public void UpcomingEndsForAnyHorizon(Type scenario, float at)
    {
        var counts = new List<int>();
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            StopAt = at + 0.1f,
            Probe = p =>
            {
                if (!p.Crossed(at)) return;
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                foreach (var horizon in new[] { 0f, 30f, 1e9f, float.PositiveInfinity })
                    counts.Add(source.Upcoming(0f, horizon).Take(100000).Count());
            },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(counts, Has.Count.EqualTo(4));
        Assert.That(counts, Has.All.LessThan(1000), string.Join(",", counts));
    }

    // The guidance run keeps every hazard it is handed in a set. Hazards that moved with a player used to be new
    // ones every frame, which grew it without end and slowed a run thirty-fold.
    [TestCase(typeof(FruP3UltimateRelativityScenario))]
    [TestCase(typeof(FruP3ApocalypseScenario))]
    public void TheGuidanceRunsHazardSetStaysSmall(Type scenario)
    {
        var seen = new HashSet<GuidanceHazard>();
        var last = 0f;
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            Probe = p =>
            {
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                foreach (var h in source.Upcoming(last, p.Time - last + 30f)) seen.Add(h);
                last = p.Time;
            },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(seen.Count, Is.LessThan(1500));
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }
}
