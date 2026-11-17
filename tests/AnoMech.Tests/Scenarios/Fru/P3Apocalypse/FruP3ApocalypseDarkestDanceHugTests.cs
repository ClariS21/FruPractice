using System.Numerics;
using AnoMech.Core.Game.Guidance;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P3Apocalypse;

namespace AnoMech.Tests;

// The party takes Darkest Dance's knockback hugging the Oracle, inside its hitbox, so it lands short of the arena edge.
public class FruP3ApocalypseDarkestDanceHugTests
{
    private const PartyRole Player = PartyRole.CasterDps;
    private const string HugMechanic = "Darkest Dance knockback";

    private static Vector2 Flat(Vector3 v) => new(v.X, v.Z);

    private static SimEnemy? Oracle(ScenarioProbe p)
        => p.World.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == FruConstants.BNpcBaseId.OracleP3);

    [Test]
    public void EveryBotStandsInsideTheOraclesHitboxWhenTheKnockbackGoesOff()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var worst = new Dictionary<PartyRole, float>();
            float hitbox = 0f;
            var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = Player,
                StopAt = FruP3ApocalypseState.DarkestDanceKnockbackCastAt + 0.05f,
                Probe = p =>
                {
                    if (!p.Crossed(FruP3ApocalypseState.DarkestDanceKnockbackCastAt) || Oracle(p) is not { } oracle) return;
                    hitbox = oracle.HitboxRadius;
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (role != Player && p.Member(role) is { } m)
                            worst[role] = Vector2.Distance(Flat(m.Position), Flat(oracle.Position));
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
            Assert.That(worst, Has.Count.EqualTo(7), $"seed {seed}");
            foreach (var (role, distance) in worst)
                Assert.That(distance, Is.LessThanOrEqualTo(hitbox - 0.3f), $"seed {seed}: {role} is {distance:F2} y from the Oracle (hitbox {hitbox:F2})");
        }
    }

    [Test]
    public void NobodyDiesAndTheReturnStackIsReachedInTimeForTheLastWater()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var atStack = new Dictionary<PartyRole, float>();
            var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = Player,
                StopAt = FruP3ApocalypseState.WaterAt(3) + 0.05f,
                Probe = p =>
                {
                    // The stack is on, and at most a room (1 y) off, the 10.5 y ring, 0.8 s ahead of the water.
                    if (!p.Crossed(FruP3ApocalypseState.WaterAt(3) - 0.8f) || Oracle(p) is not { } oracle) return;
                    foreach (var role in Enum.GetValues<PartyRole>())
                        if (role != Player && p.Member(role) is { } m)
                            atStack[role] = Vector2.Distance(Flat(m.Position), Flat(oracle.Position));
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
            Assert.That(atStack, Has.Count.EqualTo(7), $"seed {seed}");
            foreach (var (role, distance) in atStack)
                Assert.That(distance, Is.InRange(9.4f, 11.6f), $"seed {seed}: {role} is {distance:F2} y from the Oracle at the stack");
        }
    }

    [Test]
    public void ThePlayersStratTrailAndTheGuidanceRingPointAtTheHugSpot()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            StratTargetSnapshot? entry = null;
            Vector3? ringSpot = null;
            float hitbox = 0f;
            var oraclePosition = Vector3.Zero;
            var run = ScenarioRun.Execute(typeof(FruP3ApocalypseScenario), 0, seed, new ScenarioRunOptions
            {
                PlayerRole = Player,
                StopAt = 46f,
                Probe = p =>
                {
                    if (p.Crossed(44.5f) && p.Member(Player) is { } player)
                        ringSpot = GuidanceLogic.Compute(Player, Flat(player.Position), p.Time, p.World.Strat, []).Next?.Spot;
                    if (!p.Crossed(45.5f) || Oracle(p) is not { } oracle) return;
                    hitbox = oracle.HitboxRadius;
                    oraclePosition = oracle.Position;
                    if (p.World.Strat.Entries.LastOrDefault(e => e.Role == Player && e.Mechanic == HugMechanic) is { } target)
                        entry = new(target.Spot, target.Why);
                },
            });
            Assert.That(run.Passed, $"seed {seed}: {run}");
            Assert.That(entry, Is.Not.Null, $"seed {seed}: the player has no hug entry on the strat trail");
            Assert.That(entry!.Why, Is.EqualTo("Hug the boss: stand inside its hitbox for the knockback"));
            var expected = MathF.Min(1.0f, hitbox * 0.5f);
            Assert.That(Vector2.Distance(Flat(entry.Spot), Flat(oraclePosition)), Is.EqualTo(expected).Within(0.01f), $"seed {seed}");
            Assert.That(ringSpot, Is.Not.Null, $"seed {seed}");
            Assert.That(ringSpot!.Value, Is.EqualTo(entry.Spot), $"seed {seed}: the ring reads a different spot");
        }
    }

    private sealed record StratTargetSnapshot(Vector3 Spot, string? Why);
}
