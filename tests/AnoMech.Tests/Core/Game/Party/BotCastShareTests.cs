using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P3Apocalypse;
using AnoMech.Scenarios.Fru.P3Full;
using AnoMech.Scenarios.Fru.P3UltimateRelativity;

namespace AnoMech.Tests;

public class BotCastShareTests
{
    private static readonly JobId[] Casters = [JobId.WhiteMage, JobId.Scholar, JobId.BlackMage];

    // Hard casts as a share of every GCD the caster bots played, over 3 seeds.
    private static Dictionary<JobId, (int Casts, int Instants)> Measure(Type scenario, bool plans = true)
    {
        var totals = Casters.ToDictionary(j => j, _ => (Casts: 0, Instants: 0));
        foreach (var seed in new[] { 1, 2, 3 })
        {
            SimTimeline? timeline = null;
            var jobOf = new Dictionary<string, JobId>();
            ScenarioRun.Execute(scenario, 0, seed, new ScenarioRunOptions
            {
                PlayerRole = PartyRole.MeleeDpsA,
                Configure = config => config.MitigationPractice = plans,
                Probe = p =>
                {
                    timeline ??= ((FakeBattleCharas)Natives.BattleCharas).Timeline;
                    if (jobOf.Count > 0) return;
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (p.Member(role) is SimPartyNpc bot && Casters.Contains((JobId)bot.ClassJob)) jobOf[bot.EntityId.ToString("X8")] = (JobId)bot.ClassJob;
                },
            });
            foreach (var row in timeline!.Rows)
            {
                if (!jobOf.TryGetValue(row["actor"], out var job)) continue;
                var (casts, instants) = totals[job];
                if (row["kind"] == "cast") casts++;
                else if (row["kind"] == "ability" && uint.Parse(row["id"]) is var id && id is not (7 or 8)
                         && Natives.Data.Action(id) is { ActionCategory: 2 or 3, CastSeconds: 0f }) instants++;
                totals[job] = (casts, instants);
            }
        }
        return totals;
    }

    // Black Mage's long casts and the drills' constant movement keep it below half; the floor guards against
    // the departure check starving casts altogether.
    private const float Floor = 0.25f;

    [TestCase(typeof(FruP3UltimateRelativityScenario))]
    [TestCase(typeof(FruP3ApocalypseScenario))]
    [TestCase(typeof(FruP3FullScenario))]
    public void CasterBotsHardCastMostOfTheirGcds(Type scenario)
    {
        var lacking = new List<string>();
        foreach (var (job, (casts, instants)) in Measure(scenario))
        {
            var share = casts + instants == 0 ? 1f : casts / (float)(casts + instants);
            TestContext.Out.WriteLine($"SHARE {scenario.Name} {job}: {casts} casts, {instants} instants = {share:P0}");
            if (share < Floor) lacking.Add($"{job} {share:P0}");
        }
        Assert.That(lacking, Is.Empty, scenario.Name);
    }

    // A plan's presses wait for bars to end, so they cost the healers a few casts, not most of them.
    private const float AllowedDrop = 0.20f;

    [Test]
    public void PlansCostTheHealersAtMostAFewCasts()
    {
        var on = Measure(typeof(FruP3UltimateRelativityScenario), plans: true);
        var off = Measure(typeof(FruP3UltimateRelativityScenario), plans: false);
        foreach (var job in new[] { JobId.WhiteMage, JobId.Scholar })
        {
            float Share((int Casts, int Instants) t) => t.Casts + t.Instants == 0 ? 1f : t.Casts / (float)(t.Casts + t.Instants);
            TestContext.Out.WriteLine($"PLANSHARE {job}: plans on {Share(on[job]):P1} ({on[job]}), off {Share(off[job]):P1} ({off[job]})");
            Assert.That(Share(on[job]), Is.GreaterThanOrEqualTo(Share(off[job]) - AllowedDrop), job.ToString());
        }
    }
}
