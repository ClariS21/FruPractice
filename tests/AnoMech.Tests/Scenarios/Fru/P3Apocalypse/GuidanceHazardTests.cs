using System.Numerics;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;

namespace AnoMech.Tests;

public class GuidanceHazardTests
{
    [Test, Combinatorial]
    public void ReportedBoundaryMatchesBotSnapshot(
        [Values(1, 7)] int seed, [Values(false, true)] bool clockwise,
        [Values(0, 1, 3)] int rotation, [Values(false, true)] bool inside, [Values(0, 2, 5)] int wave)
    {
        GuidanceHazard? hazard = null;
        float? hitAt = null;
        bool? hit = null;
        var fires = FruP3ApocalypseState.WaveAt(wave);
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            PlayerRole = PartyRole.CasterDps, UserActionsEnabled = true, StopAt = fires + 0.15f,
            WriteArtifactsOnFailure = false,
            Overrides = o => { var s = (FruP3ApocalypseStateOverrides)o; s.Clockwise = clockwise; s.Rotation = rotation; },
            Probe = p =>
            {
                if (hazard is null)
                {
                    hazard = ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(fires - 0.1f, 0.2f).Last();
                    p.World.SimEvents.Listener = new ResolveProbe(p.World.SimEvents.Listener!, resolved =>
                    {
                        if (resolved.Action.ActionId != FruConstants.ActionId.Apocalypse || MathF.Abs(p.Time - hazard.FiresAt) > 0.05f) return;
                        if (GuidanceLogic.Xz(resolved.Origin.Position) != ((GuidanceShape.Circle)hazard.Shape).Center) return;
                        hit = resolved.Hits.Contains(p.Member(PartyRole.PhysRangedDps)!);
                        hitAt = p.Time;
                    });
                }
                var circle = (GuidanceShape.Circle)hazard.Shape;
                var bot = p.Member(PartyRole.PhysRangedDps)!;
                if (p.Time >= hazard.FiresAt - 0.15f && p.Time < hazard.FiresAt)
                {
                    var tangent = Vector2.Normalize(new Vector2(-circle.Center.Y, circle.Center.X));
                    var position = circle.Center + tangent * (circle.Radius + (inside ? -0.5f : 0.5f));
                    bot.StopMoving();
                    bot.SetPosition(new Vector3(position.X, 0, position.Y));
                }
            },
        });
        Assert.That(run.Failure, Is.Null, run.ToString());
        Assert.That(hazard, Is.Not.Null);
        Assert.That(hit, Is.EqualTo(inside), run.ToString());
        Assert.That(hitAt, Is.EqualTo(hazard!.FiresAt).Within(ScenarioRun.FrameSeconds * 2));
    }

    private sealed class ResolveProbe(IWorldEventListener inner, Action<ActionResolved> inspect) : IWorldEventListener
    {
        public void OnActionResolved(ActionResolved e) { inspect(e); inner.OnActionResolved(e); }
        public void OnStatusAdded(StatusAdded e) => inner.OnStatusAdded(e);
        public void OnStatusLost(StatusLost e) => inner.OnStatusLost(e);
        public void OnTetherEnded(TetherEnded e) => inner.OnTetherEnded(e);
    }

    [Test]
    public void FullPhaseExposesApocalypseOnTheRunClock()
    {
        GuidanceHazard[] hazards = [];
        var run = ScenarioRun.Execute(typeof(FruP3FullScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 93,
            Probe = p => { if (p.Time > 92.5f) hazards = ((IGuidanceHazardSource)p.Game.ActiveScenario!).Upcoming(120, 10).ToArray(); },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(hazards, Is.Not.Empty);
        Assert.That(hazards.Min(h => h.FiresAt), Is.EqualTo(125.3f).Within(0.04f));
    }
}
