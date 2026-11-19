using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Umad;
using AnoMech.Scenarios.Umad.P3BlackHole;
using AnoMech.Scenarios.Umad.P3Full;
using AnoMech.Scenarios.Umad.P3LimitCut;
using AnoMech.Scenarios.Umad.P4Full;
using AnoMech.Scenarios.Umad.P4KefkaSays;
using AnoMech.Scenarios.Umad.P5Celestriad;
using AnoMech.Scenarios.Umad.P5Exaflares;
using AnoMech.Scenarios.Umad.P5Flood;
using AnoMech.Scenarios.Umad.P5Full;

namespace AnoMech.Tests.Scenarios.Umad;

// Every DMU scenario reports its positional AoEs to the path guide from the casts the sim runs: when one resolves, a hazard
// with the same name and snapshot time (within 0.1 s) is on offer, its shape is the area the sim hit with, and every
// character it hit stands inside it.
public class UmadGuidanceHazardTests
{
    // Mechanics each scenario must show at least once, by the action's name.
    private static IEnumerable<TestCaseData> Scenarios() =>
    [
        new TestCaseData(typeof(UmadP3LimitCutScenario), new[] { "Ultima Blaster" }).SetName("P3 Limit Cut"),
        new TestCaseData(typeof(UmadP3BlackHoleScenario), new[] { "Slap Happy", "Nothingness", "Damning Edict" }).SetName("P3 Black Hole"),
        new TestCaseData(typeof(UmadP3FullScenario), new[] { "Stray Spray", "Shockwave", "Slap Happy", "Big Bang" }).SetName("P3 Full"),
        new TestCaseData(typeof(UmadP4KefkaSaysScenario), new[] { "Thrumming Thunder III", "Blizzard III Blowout", "Death Bolt", "Edge of Death" }).SetName("P4 Kefka Says"),
        new TestCaseData(typeof(UmadP4FullScenario), new[] { "Thrumming Thunder III", "Blizzard III Blowout", "Stray Flames" }).SetName("P4 Full"),
        new TestCaseData(typeof(UmadP5CelestriadScenario), new[] { "Quake" }).SetName("P5 Celestriad"),
        new TestCaseData(typeof(UmadP5ExaflaresScenario), new[] { "Stray Apocalypse", "Stray Entropy" }).SetName("P5 Exaflares"),
        new TestCaseData(typeof(UmadP5FloodScenario), new[] { "Flood" }).SetName("P5 Flood"),
        new TestCaseData(typeof(UmadP5FullScenario), new[] { "Flood", "Stray Apocalypse", "Forsaken Ground" }).SetName("P5 Full"),
    ];

    [Test]
    public void EveryDmuScenarioExposesHazards()
    {
        var scenarios = ScenarioCatalog.Create().Scenarios.Where(s => s.Phase.Zone is UmadZone).ToArray();
        Assert.That(scenarios, Has.Length.EqualTo(10));
        foreach (var scenario in scenarios)
            Assert.That(scenario, Is.InstanceOf<IGuidanceHazardSource>(), scenario.GetType().Name);
    }

    [TestCaseSource(nameof(Scenarios))]
    public void EachResolvedMechanicHasAHazardOfTheSameShapeAndTime(Type scenario, string[] required)
    {
        var verified = new Dictionary<string, int>();
        var problems = new List<string>();
        var hooked = false;
        var run = ScenarioRun.Execute(scenario, 0, 1, new ScenarioRunOptions
        {
            WriteArtifactsOnFailure = false,
            Probe = p =>
            {
                if (hooked) return;
                hooked = true;
                var source = (IGuidanceHazardSource)p.Game.ActiveScenario!;
                p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                {
                    var now = p.World.Events.Elapsed;
                    var name = Natives.Data.ActionName(resolved.Action.ActionId);
                    var candidates = source.Upcoming(now - 0.2f, 1f)
                        .Where(h => h.Label == name && MathF.Abs(h.FiresAt - now) <= 0.1f).ToList();
                    if (candidates.Count == 0) return;
                    var hitbox = resolved.Origin.Position == resolved.Caster.Position ? resolved.Caster.HitboxRadius : 0f;
                    var query = new AoeQuery(resolved.Action.ActionId, resolved.Origin, resolved.Action.Cast.Rotation + resolved.Action.Area.Rotation,
                        resolved.Action.Area.Size, resolved.Action.Area.CastTypeOverride, hitbox);
                    var match = candidates.Where(h => SameArea(h.Shape, query, resolved.Origin.Position)).ToList();
                    if (match.Count == 0)
                    {
                        problems.Add($"{name} at {now:F2}: no hazard of {candidates.Count} has the sim's area");
                        return;
                    }
                    var origin = new Vector2(resolved.Origin.Position.X, resolved.Origin.Position.Z);
                    var misses = resolved.Hits.Where(c =>
                    {
                        var at = new Vector2(c.Position.X, c.Position.Z);
                        return Vector2.Distance(at, origin) >= 1f && !match.Any(h => h.Shape.SignedDistance(at) <= 0.05f);
                    }).ToList();
                    if (misses.Count > 0) problems.Add($"{name} at {now:F2}: hit {misses.Count} character(s) outside its hazard");
                    verified[name] = verified.GetValueOrDefault(name) + 1;
                });
            },
        });
        Assert.That(run.Passed, run.ToString());
        TestContext.Out.WriteLine(string.Join(", ", verified.Select(v => $"{v.Key}={v.Value}")));
        Assert.That(problems, Is.Empty);
        foreach (var name in required) Assert.That(verified.GetValueOrDefault(name), Is.GreaterThan(0), $"{name} never had a matching hazard");
    }

    // The same inside/outside answer as the sim's own query on a grid around it, bar the spot a taken cast leaves open.
    private static bool SameArea(GuidanceShape shape, AoeQuery query, Vector3 origin)
    {
        for (var x = -45f; x <= 45f; x += 1.5f)
            for (var z = -45f; z <= 45f; z += 1.5f)
            {
                if (Vector2.Distance(new Vector2(x, z), new Vector2(origin.X, origin.Z)) < 1.2f) continue;
                var simInside = query.SignedDistance(new Vector3(x, 0f, z)) <= 0f;
                if ((shape.SignedDistance(new Vector2(x, z)) <= 0f) != simInside) return false;
            }
        return true;
    }

    [Test]
    public void ForsakenNullReportsItsThreePuddlesPerSetWhereTheyLand()
    {
        var seen = new HashSet<GuidanceHazard>();
        var run = ScenarioRun.Execute(typeof(UmadP5ForsakenNull), 0, 1, new ScenarioRunOptions
        {
            WriteArtifactsOnFailure = false,
            Probe = p => { foreach (var h in ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(p.Time, 30f)) seen.Add(h); },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(seen, Has.Count.EqualTo(12));
        var first = seen.Where(h => h.FiresAt < 20f).ToList();
        Assert.That(first, Has.Count.EqualTo(3));
        // The drill runs the full phase's Forsaken on a shifted clock: set 1 lands PuddlesLandAfter s after
        // its drop. The spots are rolled per run; the shared per-cast hazard checks cover their shapes.
        var lands = UmadP5Forsaken.PuddlesAt[0] - UmadP5ForsakenNull.Origin + UmadP5Forsaken.PuddlesLandAfter;
        Assert.That(first.Select(h => h.FiresAt), Has.All.EqualTo(lands).Within(0.1f));
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }
}
