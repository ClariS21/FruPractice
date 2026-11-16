using System.Globalization;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;
using AnoMech.Scenarios.Fru.P4Full;
using AnoMech.Scenarios.Fru.P5Full;

namespace AnoMech.Tests;

public class BurstWindowTests
{
    private const float Grace = 8f;
    // A bot with several burst actions plays them over a couple of GCDs.
    private const float Spread = 6f;
    private static readonly PartyRole[] PlayerRoles = [PartyRole.MeleeDpsA, PartyRole.CasterDps, PartyRole.OffTank];

    private sealed record Use(PartyRole Role, JobId Job, uint Id, float Time);

    private sealed record Observed(IReadOnlyList<BurstAnchor> Anchors, List<Use> Uses, Dictionary<PartyRole, JobId> Jobs,
        Dictionary<(PartyRole Role, string Window, bool Raid), float> Plans);

    private static bool IsRaid(uint id) => Natives.Data.Action(id)!.RecastSeconds >= 110f;

    private static Observed Observe(Type scenarioType, int seed, float stopAt)
    {
        IReadOnlyList<BurstAnchor> anchors = [];
        SimTimeline? timeline = null;
        var jobs = new Dictionary<PartyRole, JobId>();
        var entities = new Dictionary<string, PartyRole>();
        var plans = new Dictionary<(PartyRole, string, bool), float>();
        var result = ScenarioRun.Execute(scenarioType, 0, seed, new ScenarioRunOptions
        {
            StopAt = stopAt,
            PlayerRole = PlayerRoles[(seed - 1) % PlayerRoles.Length],
            Probe = p =>
            {
                timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                if (anchors.Count == 0) anchors = p.Game.Catalog.Scenarios.Single(s => s.GetType() == scenarioType).BurstAnchors;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    if (p.Member(role) is not SimPartyNpc bot) continue;
                    jobs[role] = (JobId)bot.ClassJob;
                    entities[bot.EntityId.ToString("X8")] = role;
                    if (p.World.Party.Combat?.PlannedBursts(bot) is { Count: > 0 } planned)
                        foreach (var burst in planned) plans.TryAdd((role, burst.Window, burst.Raid), burst.At);
                }
            },
        });
        TestContext.Out.WriteLine($"RUN {scenarioType.Name} seed {seed}: elapsed {result.Elapsed:F1}, failure {result.Failure}, deaths {string.Join(",", result.Deaths.Select(d => $"{d.Role}@{d.Time:F1}"))}, mech {string.Join(",", result.MechanicFailures.Select(m => m.Reason))}");
        var uses = new List<Use>();
        foreach (var row in timeline!.Rows.Where(r => r["kind"] == "ability" && entities.ContainsKey(r["actor"])))
        {
            var role = entities[row["actor"]];
            var id = uint.Parse(row["id"]);
            if (BotScripts.For((byte)jobs[role])!.Burst.Contains(id))
                uses.Add(new Use(role, jobs[role], id, float.Parse(row["t"], CultureInfo.InvariantCulture)));
        }
        return new Observed(anchors, uses, jobs, plans);
    }

    private static (float From, float To)? Interval(BurstWindow window, IReadOnlyList<BurstAnchor> anchors, bool raid)
    {
        if (window.AnchorTime(anchors) is not { } anchor) return null;
        BurstRange? range = raid ? window.Raid : window.Personal;
        return range is { } r ? (MathF.Max(0f, anchor + r.From), anchor + r.To + Grace + Spread) : null;
    }

    [TestCase(typeof(FruP3FullScenario), 175f)]
    [TestCase(typeof(FruP4FullScenario), 185f)]
    [TestCase(typeof(FruP5FullScenario), 275f)]
    public void EveryWindowFiresInItsRangeAndNothingFiresOutsideOne(Type scenarioType, float stopAt)
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var seen = Observe(scenarioType, seed, stopAt);
            Assert.That(seen.Uses, Is.Not.Empty, $"seed {seed}");
            foreach (var use in seen.Uses)
            {
                var inSome = BurstWindow.All.Any(w => Interval(w, seen.Anchors, IsRaid(use.Id)) is { } i && use.Time >= i.From - 0.01f && use.Time <= i.To);
                Assert.That(inSome, $"seed {seed}: {use.Job} used {Natives.Data.ActionName(use.Id)} at {use.Time:F1} outside any window");
            }
            foreach (var window in BurstWindow.All)
            foreach (var raid in new[] { true, false })
            {
                if (Interval(window, seen.Anchors, raid) is not { } interval) continue;
                if (interval.From > stopAt - 20f) continue;
                var expected = seen.Jobs.SelectMany(j => BotScripts.For((byte)j.Value)!.Burst.Where(id => IsRaid(id) == raid).Select(id => (Role: j.Key, Id: id))).ToList();
                var fired = expected.Count(e => seen.Uses.Any(u => u.Role == e.Role && u.Id == e.Id && u.Time >= interval.From - 0.01f && u.Time <= interval.To));
                Assert.That(fired, Is.GreaterThanOrEqualTo(expected.Count * 7 / 10), $"seed {seed} {window.Name} {(raid ? "raid" : "personal")}: {fired}/{expected.Count} fired in {interval.From:F1}..{interval.To:F1}");
            }
        }
    }

    [Test]
    public void P4HoldsItsRaidBuffsBetweenTheTwoWindows()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var seen = Observe(typeof(FruP4FullScenario), seed, 185f);
            var raid = seen.Uses.Where(u => IsRaid(u.Id)).ToList();
            Assert.That(raid, Is.Not.Empty, $"seed {seed}");
            Assert.That(raid.Where(u => u.Time > 45f), Is.Empty, $"seed {seed}: no raid buffs after the Darklit Dragonsong window, around 146 s included");
        }
    }

    [Test]
    public void RadiantFinaleLinesUpWithTheOtherRaidBuffs()
    {
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var seen = Observe(typeof(FruP5FullScenario), seed, 275f);
            var finales = seen.Uses.Where(u => u.Id == 25785).ToList();
            Assert.That(finales.Count, Is.EqualTo(3), $"seed {seed}: Radiant Finale in all three raid windows");
            foreach (var finale in finales)
            {
                var others = seen.Uses.Where(u => u.Id != 25785 && IsRaid(u.Id) && Math.Abs(u.Time - finale.Time) < 15f).ToList();
                Assert.That(others, Is.Not.Empty, $"seed {seed}");
                Assert.That(others.Max(u => Math.Abs(u.Time - finale.Time)), Is.LessThan(Grace + Spread), $"seed {seed}: Radiant Finale at {finale.Time:F1}");
            }
        }
    }

    [Test]
    public void ADrillAndItsFullPhasePlaceAWindowTheSameDistanceFromTheAnchor()
    {
        var drill = Observe(typeof(FruP3UltimateRelativityScenario), 1, 70f);
        var full = Observe(typeof(FruP3FullScenario), 1, 80f);
        var drillAnchor = drill.Anchors.Single().Time;
        var fullAnchor = full.Anchors.Single(a => a.ActionId == drill.Anchors.Single().ActionId).Time;
        var compared = 0;
        foreach (var ((role, window, raid), at) in drill.Plans.Where(p => p.Key.Window == "P3-B"))
        {
            Assert.That(full.Plans.TryGetValue((role, window, raid), out var fullAt), $"{role} {window}");
            Assert.That(fullAt - fullAnchor, Is.EqualTo(at - drillAnchor).Within(0.001f), $"{role} {window} {(raid ? "raid" : "personal")}");
            compared++;
        }
        Assert.That(compared, Is.GreaterThan(0));
    }

    [Test]
    public void APickDoesNotDependOnWhatElseTheStreamWasAsked()
    {
        var world = new SimWorld(new EventScheduler());
        world.Reseed(9);
        var range = new BurstRange(5.9f, 7.9f);
        var first = BurstWindow.Pick(world.Stream("bot-combat"), PartyRole.PhysRangedDps, "P5-C", true, range);
        world.Stream("bot-combat").NextSingle();
        var second = BurstWindow.Pick(world.Stream("bot-combat"), PartyRole.PhysRangedDps, "P5-C", true, range);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Is.InRange(5.9f, 7.9f));
    }

    [Test]
    public void AWindowNeedsItsAnchorCastInTheScenario()
    {
        var window = BurstWindow.All.Single(w => w.Name == "P3-A");
        Assert.That(window.AnchorTime([new BurstAnchor(40265u, 2f)]), Is.EqualTo(2f));
        Assert.That(window.AnchorTime([]), Is.Null);
        Assert.That(BurstWindow.All.Single(w => w.Name == "P4-B").Personal.From + 1f, Is.LessThan(0f), "P4-B reaches back before a drill that starts at its anchor");
    }
}
