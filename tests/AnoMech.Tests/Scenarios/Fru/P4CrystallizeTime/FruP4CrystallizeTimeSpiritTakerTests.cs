using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Fru;
using AnoMech.Scenarios.Fru.P4CrystallizeTime;
using AnoMech.Scenarios.Fru.P4Full;

namespace AnoMech.Tests;

public class FruP4CrystallizeTimeSpiritTakerTests
{
    private const float SnapshotAt = 48.5f;
    private const float SpotTolerance = 1.5f;
    private const float Radius = FruConstants.Geometry.ArenaRadius;

    // (forward, right) as fractions of the arena radius for the left group (negative right); the right group mirrors the right axis.
    private static (float Forward, float Right) Layout(PartyRole role) => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => (0.82f, 0.22f),
        PartyRole.RegenHealer or PartyRole.ShieldHealer => (0.18f, 0.22f),
        PartyRole.MeleeDpsA or PartyRole.MeleeDpsB => (-0.19f, 0.34f),
        _ => (-0.04f, 0.87f),
    };

    private static bool LeftGroup(PartyRole role) => role is PartyRole.MainTank or PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.PhysRangedDps;

    // Forward runs from the centre towards the corner the rewinds are placed in; right is forward turned clockwise seen from above.
    private static Vector3 Spot(PartyRole role, CrystallizeCorner corner)
    {
        var east = corner is CrystallizeCorner.NE or CrystallizeCorner.SE ? 1f : -1f;
        var north = corner is CrystallizeCorner.NW or CrystallizeCorner.NE ? 1f : -1f;
        var forward = Vector3.Normalize(new Vector3(east, 0, -north));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var (f, r) = Layout(role);
        return (forward * f + right * r * (LeftGroup(role) ? -1f : 1f)) * Radius;
    }

    private static float Flat(Vector3 a, Vector3 b) => Vector3.Distance(a with { Y = 0 }, b with { Y = 0 });

    [Test]
    public void BotsSpreadToTheStratSpotsForSpiritTaker([Range(1, 10)] int seed)
    {
        var corner = (CrystallizeCorner)(seed % 4);
        var playerRole = seed % 2 == 0 ? PartyRole.MainTank : PartyRole.CasterDps;
        var positions = new Dictionary<PartyRole, Vector3>();
        var fragment = Vector3.Zero;
        var fragmentRadius = 0f;
        var radius = 0f;
        var run = ScenarioRun.Execute(typeof(FruP4CrystallizeTimeScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = SnapshotAt + 0.1f,
            PlayerRole = playerRole,
            Overrides = o => ((FruP4CrystallizeTimeStateOverrides)o).Corner = corner,
            Probe = p =>
            {
                if (!p.Crossed(SnapshotAt - 0.01f)) return;
                radius = Natives.Data.Action(FruConstants.ActionId.SpiritTaker)!.EffectRange;
                var crystal = p.World.Children.OfType<SimEnemy>().First(e => e.BNpcBaseId == FruConstants.BNpcBaseId.FragmentOfFate);
                (fragment, fragmentRadius) = (crystal.Position, crystal.HitboxRadius);
                foreach (var role in Enum.GetValues<PartyRole>())
                    if (role != playerRole && p.Member(role) is { } member) positions[role] = member.Position;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(positions, Has.Count.EqualTo(7));
        foreach (var (role, position) in positions)
        {
            var spot = Spot(role, corner);
            Assert.That(Flat(position, spot), Is.LessThanOrEqualTo(SpotTolerance), $"{role} at {position} for {spot} ({corner})");
            Assert.That(Flat(spot, fragment), Is.GreaterThan(radius + fragmentRadius), $"{role}'s spot is in Spirit Taker's reach of the Fragment ({corner})");
            Assert.That(Flat(position, fragment), Is.GreaterThan(radius + fragmentRadius), $"{role} stands in Spirit Taker's reach of the Fragment ({corner})");
        }
        var roles = positions.Keys.ToList();
        var closest = float.MaxValue;
        foreach (var a in roles)
            foreach (var b in roles.Where(b => b > a))
                closest = MathF.Min(closest, Flat(positions[a], positions[b]));
        TestContext.Out.WriteLine($"{corner}: Spirit Taker radius {radius}, Fragment hitbox {fragmentRadius}, closest pair {closest:F1}y, closest spot to the Fragment {Enum.GetValues<PartyRole>().Min(r => Flat(Spot(r, corner), fragment)):F1}y");
        Assert.That(closest, Is.GreaterThan(radius), "two bots within one Spirit Taker radius of each other");
    }

    [Test]
    public void TheSameSpreadHoldsInTheFullPhase([Values(1, 2, 3)] int seed)
    {
        var positions = new Dictionary<PartyRole, Vector3>();
        var radius = 0f;
        var at = FruP4FullScenario.CrystallizeTimeAt + SnapshotAt;
        var run = ScenarioRun.Execute(typeof(FruP4FullScenario), 0, seed, new ScenarioRunOptions
        {
            StopAt = at + 0.1f,
            PlayerRole = PartyRole.CasterDps,
            Probe = p =>
            {
                if (!p.Crossed(at - 0.01f)) return;
                radius = Natives.Data.Action(FruConstants.ActionId.SpiritTaker)!.EffectRange;
                foreach (var role in Enum.GetValues<PartyRole>())
                    if (role != PartyRole.CasterDps && p.Member(role) is { } member) positions[role] = member.Position;
            },
        });
        Assert.That(run.Passed, run.ToString);
        Assert.That(positions, Has.Count.EqualTo(7));
        foreach (var a in positions.Keys)
            foreach (var b in positions.Keys.Where(b => b > a))
                Assert.That(Flat(positions[a], positions[b]), Is.GreaterThan(radius), $"{a} and {b}");
    }
}
