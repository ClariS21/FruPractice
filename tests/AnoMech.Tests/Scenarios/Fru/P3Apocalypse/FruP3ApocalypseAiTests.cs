using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

public class FruP3ApocalypseAiTests
{
    private const float FrameSlack = 1f / 30f;

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void FlexSwappersLeaveOneAndAHalfToTwoSecondsAfterDebuffs(int seed)
    {
        var debuffsAt = FruP3ApocalypseState.WaterAppliedAt;
        var atDebuffs = new Dictionary<PartyRole, System.Numerics.Vector3>();
        var departures = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = 16.7f,
            Probe = p =>
            {
                if (p.Time < debuffsAt) return;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    if (p.Member(role) is not { } member) continue;
                    if (!atDebuffs.TryGetValue(role, out var start)) atDebuffs[role] = member.Position;
                    else if (!departures.ContainsKey(role) && Vector3Distance(start, member.Position) > 0.05f) departures[role] = p.Time - debuffsAt;
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(departures, Is.Not.Empty);
        foreach (var (role, delay) in departures)
            Assert.That(delay, Is.InRange(1.5f - FrameSlack, 2.0f + 2 * FrameSlack), $"{role} departed {delay:F2}s after the debuffs");
    }

    [Test, Combinatorial]
    public void BotsCollapseIntoTheirStacksRightAfterTheFlex(
        [Values(0, 1, 2, 3)] int seed,
        [Values(PartyRole.CasterDps, PartyRole.MainTank)] PartyRole player)
    {
        var centres = new[]
        {
            Centre(DefaultSlots[..4]),
            Centre(DefaultSlots[4..]),
        };
        var worst = new Dictionary<PartyRole, float>();
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = 18.7f,
            PlayerRole = player,
            Probe = p =>
            {
                if (!p.Crossed(18.6f)) return;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    if (role == player || p.Member(role) is not { } member) continue;
                    var position = member.Position;
                    worst[role] = centres.Min(c => Vector3Distance(c, position));
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(worst, Is.Not.Empty);
        foreach (var (role, distance) in worst)
            Assert.That(distance, Is.LessThan(1.5f), $"{role} is {distance:F2}y from its stack at 18.6s");
    }

    private static System.Numerics.Vector3 Centre((PartyRole Role, float X, float Z)[] slots)
        => new(slots.Average(s => s.X), 0, slots.Average(s => s.Z));

    private static readonly (PartyRole Role, float X, float Z)[] DefaultSlots =
    [
        (PartyRole.MainTank, -4.43f, -1.95f), (PartyRole.OffTank, -4.25f, 3.47f), (PartyRole.RegenHealer, -9.74f, -2.51f), (PartyRole.ShieldHealer, -8.74f, 3.01f),
        (PartyRole.MeleeDpsA, 6.22f, 2.50f), (PartyRole.MeleeDpsB, 6.10f, -1.87f), (PartyRole.PhysRangedDps, 9.73f, 2.95f), (PartyRole.CasterDps, 9.85f, -2.17f),
    ];

    [Test]
    public void BotsSettleNearTheirSetupSlotsBeforeDebuffs()
    {
        var settled = new Dictionary<PartyRole, System.Numerics.Vector3>();
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, 1, new ScenarioRunOptions
        {
            StopAt = 12.7f,
            Probe = p =>
            {
                if (p.Time < 12.6f) return;
                foreach (var role in Enum.GetValues<PartyRole>())
                    if (p.Member(role) is { } member) settled[role] = member.Position;
            },
        });
        Assert.That(run.Passed, run.ToString);
        foreach (var (role, x, z) in DefaultSlots)
        {
            TestContext.Out.WriteLine($"{role}: ({settled[role].X:F2}, {settled[role].Z:F2}) slot ({x}, {z})");
            Assert.That(System.Numerics.Vector3.Distance(settled[role], new System.Numerics.Vector3(x, 0, z)), Is.LessThan(2.5f), role.ToString());
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void BotsRunBackTheMomentTheyLandInsideTheArena(int seed)
    {
        var landed = new Dictionary<PartyRole, System.Numerics.Vector3>();
        var delays = new Dictionary<PartyRole, float>();
        var stand = new Dictionary<PartyRole, System.Numerics.Vector3>();
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = 50f,
            Probe = p =>
            {
                if (p.Crossed(FruP3ApocalypseState.DarkestDanceKnockbackCastAt - 0.1f))
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (p.Member(role) is { } m) stand[role] = m.Position;
                var landsAt = FruP3ApocalypseState.DarkestDanceLandsAt;
                foreach (var role in Enum.GetValues<PartyRole>())
                {
                    if (p.Member(role) is not { } m) continue;
                    if (p.Crossed(landsAt)) landed[role] = m.Position;
                    else if (landed.TryGetValue(role, out var at) && !delays.ContainsKey(role) && Vector3Distance(at, m.Position) > 0.05f)
                        delays[role] = p.Time - landsAt;
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(landed, Has.Count.EqualTo(8));
        TestContext.Out.WriteLine($"stand supports {Centre(stand, 0, 4)} dps {Centre(stand, 4, 8)}; landed supports {Centre(landed, 0, 4)} dps {Centre(landed, 4, 8)}");
        foreach (var (role, at) in landed)
        {
            TestContext.Out.WriteLine($"{role} landed ({at.X:F1}, {at.Z:F1}) r={at.Length():F1} started after {delays.GetValueOrDefault(role, -1f):F3}");
            Assert.That(at.Length(), Is.LessThan(19.8f), $"{role} landing");
            Assert.That(delays.GetValueOrDefault(role, 9f), Is.LessThan(0.1f), $"{role} delay");
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void MeleeBotsGapCloseOnLandingThenReachTheFinalStackBeforeTheLastWater(int seed)
        => AssertGapCloseThenStack(seed, PartyRole.CasterDps, [PartyRole.MeleeDpsA, PartyRole.MeleeDpsB]);

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void TankBotsGapCloseOnLandingThenReachTheFinalStackBeforeTheLastWater(int seed)
        => AssertGapCloseThenStack(seed, PartyRole.CasterDps, [PartyRole.MainTank, PartyRole.OffTank]);

    private static void AssertGapCloseThenStack(int seed, PartyRole player, PartyRole[] melee)
    {
        var closest = new Dictionary<PartyRole, float>();
        var atWater = new Dictionary<PartyRole, float>();
        var landed = new Dictionary<PartyRole, float>();
        var landsAt = FruP3ApocalypseState.DarkestDanceLandsAt;
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = FruP3ApocalypseState.WaterAt(3) + 0.1f,
            PlayerRole = player,
            Probe = p =>
            {
                var oracle = p.World.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == FruConstants.BNpcBaseId.OracleP3);
                if (oracle == null || p.Time < landsAt) return;
                foreach (var role in melee)
                {
                    if (p.Member(role) is not { } m) continue;
                    var distance = System.Numerics.Vector3.Distance(m.Position, oracle.Position);
                    if (!landed.ContainsKey(role)) landed[role] = distance;
                    if (p.Time <= landsAt + 0.7f) closest[role] = Math.Min(closest.GetValueOrDefault(role, float.MaxValue), distance);
                    if (p.Time >= FruP3ApocalypseState.WaterAt(3) - 0.05f) atWater[role] = distance;
                }
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(closest, Has.Count.EqualTo(2));
        foreach (var role in melee)
        {
            TestContext.Out.WriteLine($"{role}: closest {closest[role]:F1}, at the last water {atWater[role]:F1}");
            Assert.That(closest[role], Is.LessThan(landed[role] - 15f), $"{role} dashed in from {landed[role]:F1}");
            // The final stack sits 10.5 y from the Oracle (user's call); allow the landing jitter either side.
            Assert.That(atWater[role], Is.InRange(9.5f, 11.5f), $"{role} is back on the stack");
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    public void FirstStackCollapsesToEachGroupsCentreNotTheBoss(int seed)
    {
        var atWater = new Dictionary<PartyRole, System.Numerics.Vector3>();
        var oraclePosition = System.Numerics.Vector3.Zero;
        var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = FruP3ApocalypseState.WaterAt(1) + 0.1f,
            PlayerRole = PartyRole.CasterDps,
            Probe = p =>
            {
                var oracle = p.World.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == FruConstants.BNpcBaseId.OracleP3);
                if (oracle == null || p.Time < FruP3ApocalypseState.WaterAt(1) - 0.05f) return;
                oraclePosition = oracle.Position;
                foreach (var role in Enum.GetValues<PartyRole>())
                    if (role != PartyRole.CasterDps && p.Member(role) is { } m) atWater[role] = m.Position;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(atWater, Has.Count.EqualTo(7));
        foreach (var (from, to) in new[] { (0, 4), (4, 8) })
        {
            var centre = new System.Numerics.Vector3(
                DefaultSlots[from..to].Average(s => s.X) + oraclePosition.X, 0, DefaultSlots[from..to].Average(s => s.Z) + oraclePosition.Z);
            TestContext.Out.WriteLine($"group {from}-{to}: centre ({centre.X - oraclePosition.X:F2}, {centre.Z - oraclePosition.Z:F2}), {Vector3Distance(centre, oraclePosition):F2} from the Oracle");
            Assert.That(Vector3Distance(centre, oraclePosition), Is.GreaterThanOrEqualTo(6.5f), $"group {from}-{to} centre from the Oracle");
            // Flex swaps move a role to the other group, so a bot's group is the side it stacks on.
            foreach (var (role, position) in atWater.Where(r => (r.Value.X < oraclePosition.X) == (from == 0)))
                Assert.That(Vector3Distance(position, centre), Is.LessThanOrEqualTo(1.0f), $"{role} at the first water");
        }
    }

    private static string Centre(Dictionary<PartyRole, System.Numerics.Vector3> positions, int from, int to)
    {
        var group = Enumerable.Range(from, to - from).Select(i => positions[(PartyRole)i]).ToList();
        return $"({group.Average(v => v.X):F2}, {group.Average(v => v.Z):F2})";
    }

    private static float Vector3Distance(System.Numerics.Vector3 a, System.Numerics.Vector3 b) => System.Numerics.Vector3.Distance(a, b);
}
