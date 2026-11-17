using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P5FulgentBlade;
using AnoMech.Scenarios.Fru.P5Full;
using AnoMech.Scenarios.Fru.P5ParadiseRegained;

namespace AnoMech.Tests;

// The reported hazards match the sim's damage: a bot put 0.5 y inside what P5 reports just before it
// fires is hit by the action that fires, and one put 0.5 y outside every hazard firing then is not.
public class FruP5GuidanceHazardTests
{
    private static readonly uint[] StripActions =
    [
        FruConstants.ActionId.PathOfLightFirst, FruConstants.ActionId.PathOfDarknessFirst,
        FruConstants.ActionId.PathOfLightRest, FruConstants.ActionId.PathOfDarknessRest,
    ];
    private static readonly uint[] WingActions = [FruConstants.ActionId.WingsCleaveLight, FruConstants.ActionId.WingsCleaveDark];
    private static readonly uint[] LineActions = [40317u, 40318u, 40119u, 40120u];

    private static void Force(object overrides, int layout)
    {
        var o = (FruP5FulgentBladeStateOverrides)overrides;
        o.PositionIndex = layout % 4;
        o.RotationIndex = layout / 4 % 4;
        o.Mirrored = layout / 16 == 1;
    }

    // Runs to just past `fires`, putting PhysRangedDps 0.5 y inside or outside the union of the hazards
    // that fire then, and says whether an action of `ids` firing then hit it.
    private static (bool Fired, bool Hit, int Count) Probe(Type scenario, int seed, Action<object>? overrides, float fires, string label, uint[] ids, bool inside)
    {
        var hit = false;
        var fired = false;
        var count = 0;
        var installed = false;
        var run = ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.CasterDps, StopAt = fires + 0.15f, WriteArtifactsOnFailure = false,
            Overrides = overrides,
            Probe = p =>
            {
                if (!installed)
                {
                    installed = true;
                    p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                    {
                        if (!ids.Contains(resolved.Action.ActionId) || MathF.Abs(p.Time - fires) > 0.05f) return;
                        fired = true;
                        hit |= resolved.Hits.Contains(p.Member(PartyRole.PhysRangedDps)!);
                    });
                }
                if (p.Time < fires - 0.15f || p.Time >= fires) return;
                var hazards = ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(p.Time, 0.3f)
                    .Where(h => h.Label == label && MathF.Abs(h.FiresAt - fires) < 0.02f).ToArray();
                count = hazards.Length;
                if (hazards.Length == 0) return;
                var target = inside ? -0.5f : 0.5f;
                Vector2? best = null;
                var bestError = float.MaxValue;
                for (var x = -19f; x <= 19f; x += 0.1f)
                    for (var z = -19f; z <= 19f; z += 0.1f)
                    {
                        var point = new Vector2(x, z);
                        if (point.LengthSquared() > 19f * 19f) continue;
                        var error = MathF.Abs(hazards.Min(h => h.Shape.SignedDistance(point)) - target);
                        if (error >= bestError) continue;
                        bestError = error;
                        best = point;
                    }
                var bot = p.Member(PartyRole.PhysRangedDps)!;
                bot.StopMoving();
                bot.SetPosition(new Vector3(best!.Value.X, 0f, best.Value.Y));
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        return (fired, hit, count);
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }

    [Test, Combinatorial]
    public void ExalineBandsMatchTheStrips(
        [Values(0, 5, 10, 17, 22, 27, 31)] int layout, [Values(0, 5, 11)] int which, [Values(false, true)] bool inside)
    {
        var (group, hit) = which switch { 0 => (0, 0), 5 => (1, 2), _ => (2, 3) };
        var fires = FruP5FulgentBladeHazards.FiresAt(group, hit);
        var (fired, wasHit, count) = Probe(typeof(FruP5FulgentBladeScenario), 1, o => Force(o, layout), fires, "Exaline", StripActions, inside);
        Assert.That(count, Is.GreaterThan(0));
        Assert.That(fired, Is.True, "no strip fired at the reported time");
        Assert.That(wasHit, Is.EqualTo(inside));
    }

    [Test, Combinatorial]
    public void WingCleavesMatchTheCones(
        [Values(false, true)] bool darkFirst, [Values(0, 1)] int wing, [Values(1, 2)] int seed, [Values(false, true)] bool inside)
    {
        var fires = FruP5ParadiseRegainedHazards.CleaveAt(wing);
        var (fired, wasHit, count) = Probe(typeof(FruP5ParadiseRegainedScenario), seed,
            o => ((FruP5ParadiseRegainedStateOverrides)o).DarkFirst = darkFirst, fires, "Wings cleave", WingActions, inside);
        Assert.That(count, Is.EqualTo(1));
        Assert.That(fired, Is.True);
        Assert.That(wasHit, Is.EqualTo(inside));
    }

    [Test, Combinatorial]
    public void PolarizingLinesMatchTheRectangles(
        [Values(0, 2)] int round, [Values(false, true)] bool echo, [Values(1, 2)] int seed, [Values(false, true)] bool inside)
    {
        var fires = FruP5ParadiseRegainedState.LinesAt(round) + (echo ? 2.6f : FruP5ParadiseRegainedHazards.LineFirstHitAfter);
        var (fired, wasHit, count) = Probe(typeof(FruP5ParadiseRegainedScenario), seed, null, fires, "Cruel Path", LineActions, inside);
        Assert.That(count, Is.EqualTo(2));
        Assert.That(fired, Is.True);
        Assert.That(wasHit, Is.EqualTo(inside));
    }

    [Test]
    public void FullPhaseForwardsTheRunningSlicesHazards()
    {
        var labels = new HashSet<string>();
        var run = ScenarioRun.Execute(typeof(FruP5FullScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 60,
            Probe = p => { foreach (var h in ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(p.Time, 3f)) labels.Add(h.Label); },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(labels, Is.SupersetOf(new[] { "Exaline", "Wings cleave" }));
    }
}
