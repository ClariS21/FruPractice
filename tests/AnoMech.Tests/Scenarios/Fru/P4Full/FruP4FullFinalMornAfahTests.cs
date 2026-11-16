using AnoMech.Core.Game.Party;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios.Fru.P4Full;
using static AnoMech.Core.UserActions.JobId;

namespace AnoMech.Tests;

public class FruP4FullFinalMornAfahTests
{
    // Each Morn Afah has a 5.7 s bar and its damage lands 1.8 s after it: cast 74.8 s lands at 82.3 s, cast 157.5 s at 165.0 s.
    private const float FirstMornAfahDamage = 82.3f;
    private const float FinalMornAfahDamage = 165.0f;

    [TestCase(PartyRole.MainTank, true)]
    [TestCase(PartyRole.CasterDps, false)]
    public void WholePartyTakesTheFirstMornAfahAtCentre(PartyRole player, bool gnbPld) => AssertAtCentre(player, gnbPld, FirstMornAfahDamage);

    [TestCase(PartyRole.MainTank, true)]
    [TestCase(PartyRole.CasterDps, false)]
    public void WholePartyTakesTheFinalMornAfahAtCentre(PartyRole player, bool gnbPld) => AssertAtCentre(player, gnbPld, FinalMornAfahDamage);

    private static void AssertAtCentre(PartyRole player, bool gnbPld, float at)
    {
        IReadOnlyDictionary<PartyRole, JobId>? jobs = gnbPld
            ? new Dictionary<PartyRole, JobId> { [PartyRole.MainTank] = Gunbreaker, [PartyRole.OffTank] = Paladin }
            : null;
        for (var seed = 0; seed < 4; seed++)
        {
            var dist = new Dictionary<PartyRole, float>();
            ScenarioRun.Execute(typeof(FruP4FullScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = player,
                JobOverrides = jobs,
                UserActionsEnabled = true,
                Configure = c => c.MitigationPractice = true,
                StopAt = at + 0.2f,
                Probe = p =>
                {
                    if (!p.Crossed(at)) return;
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (role != player && p.Member(role) is { } m)
                            dist[role] = MathF.Sqrt(m.Position.X * m.Position.X + m.Position.Z * m.Position.Z);
                },
            });
            var text = $"{player} seed {seed} at {at}: " + string.Join(", ", dist.Select(kv => $"{kv.Key}={kv.Value:F2}"));
            TestContext.Out.WriteLine(text);
            Assert.That(dist, Has.Count.EqualTo(7), text);
            Assert.That(dist.Values.All(d => d <= 1.5f), Is.True, text);
        }
    }
}
