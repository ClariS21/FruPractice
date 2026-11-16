using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Tests;

public class BotCombatRunEndTests
{
    private const float AfterEndSeconds = 15f;
    private const float Frame = 1f / 60f;

    [Test]
    public void BotsAttackWhileTheRunPlaysAndStopOnceItIsDone()
    {
        SimTimeline? timeline = null;
        var duringRun = 0;
        var afterEnd = 0;
        var ended = false;
        var run = ScenarioRun.Execute(typeof(FruP3UltimateRelativityScenario), 0, 1, new ScenarioRunOptions
        {
            Probe = p =>
            {
                timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                if (ended || p.Game.Status != SessionStatus.Succeeded) return;
                ended = true;
                duringRun = Attacks(timeline, p);
                for (var t = 0f; t < AfterEndSeconds; t += Frame) p.Game.Tick(Frame);
                afterEnd = Attacks(timeline, p) - duringRun;
            },
        });
        Assert.That(run.Passed, run.ToString());
        Assert.That(ended, "the run never completed");
        Assert.That(duringRun, Is.GreaterThan(0), "bots should fight while the run plays");
        Assert.That(afterEnd, Is.Zero, "bots kept attacking after the run completed");
    }

    [Test]
    public void BotsKeepAttackingAfterANonFreezingFailure()
    {
        SimTimeline? timeline = null;
        var atFailure = -1;
        var atCheck = -1;
        ScenarioRun.Execute(typeof(FruP3UltimateRelativityScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 16f,
            Probe = p =>
            {
                timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                if (p.Crossed(5f))
                {
                    p.World.Fail("test soft failure");
                    atFailure = Attacks(timeline, p);
                }
                if (p.Crossed(15f)) atCheck = Attacks(timeline, p);
            },
        });
        Assert.That(atFailure, Is.GreaterThanOrEqualTo(0), "the probe never reached the failure point");
        Assert.That(atCheck - atFailure, Is.GreaterThan(0), "bots stopped attacking after a failure that does not end the run");
    }

    private static int Attacks(SimTimeline timeline, ScenarioProbe p)
    {
        var bots = p.World.Party.AllMembers().OfType<SimPartyNpc>().Select(b => b.EntityId.ToString("X8")).ToHashSet();
        return timeline.Rows.Count(r => r["kind"] == "ability" && bots.Contains(r["actor"]) && r["damage_type"] == "");
    }
}
