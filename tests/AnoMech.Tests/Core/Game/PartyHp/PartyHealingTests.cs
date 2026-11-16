using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;

namespace AnoMech.Tests;

public class PartyHealingTests
{
    private const uint ChaosP3BNpcBaseId = 19508;
    private const float Step = 0.01f;
    private const int Seed = 77;

    private static readonly EnemyAction Pulsar = FruActions.ShockwavePulsar with
    {
        Cast = new() { CastSecondsOverride = 1f },
        Effects = [Damage()],
        Timing = new() { DamageDelay = 1f },
    };

    private FakeGame fake = null!;
    private SimWorld world = null!;
    private SimEnemy boss = null!;
    private float now;

    [SetUp]
    public void InstallGame()
    {
        fake = FakeGame.Install();
        fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
        var game = new Game();
        DalamudServices.Install(nameof(Plugin.Game), game);
        world = game.World;
        world.Reseed(Seed);
        world.BotCombatEnabled = false;
        world.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
        boss = world.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
        now = 0f;
        Advance(0.5f);
    }

    [TearDown]
    public void RemoveGame()
    {
        world.Despawn();
        DalamudServices.Install(nameof(Plugin.Game), null!);
    }

    private IEnumerable<SimCharacter> Members => Enum.GetValues<PartyRole>().Select(r => world.Party.Get(r)!);

    private static uint Hp(SimCharacter who) => who.Proxy!.Health;

    private void Advance(float seconds)
    {
        for (var left = seconds; left > 0f; left -= Step)
        {
            world.Events.Tick(Step);
            world.Tick(Step);
            now += Step;
        }
    }

    [Test]
    public void NaturalRegenGivesOnePercentOnEachBeatAndNothingBetween()
    {
        var start = Members.ToDictionary(member => member, member =>
        {
            member.WriteHealth(member.Proxy!.MaxHealth / 2);
            return Hp(member);
        });
        var phase = world.Healing.TickPhase;
        var beat = phase + MathF.Ceiling((world.Events.Elapsed - phase + 0.001f) / 3f) * 3f;
        while (world.Events.Elapsed < beat + 0.5f) Advance(Step);
        foreach (var member in Members)
            Assert.That(Hp(member) - start[member], Is.EqualTo((uint)MathF.Round(0.01f * member.Proxy!.MaxHealth)),
                "every bot and the player seat gain exactly 1% of max HP");
        var after = Members.ToDictionary(member => member, Hp);
        while (world.Events.Elapsed < beat + 2.9f) Advance(Step);
        foreach (var member in Members) Assert.That(Hp(member), Is.EqualTo(after[member]), "HP holds between server beats");
    }

    [Test]
    public void NaturalRegenSkipsTheDeadAndCapsAtFull()
    {
        var alive = world.Party.Player!;
        var dead = Members.Last();
        var max = alive.Proxy!.MaxHealth;
        alive.WriteHealth((uint)MathF.Round(max * 0.995f));
        dead.Die(0);
        var deadHp = Hp(dead);
        Advance(4f);
        Assert.That(Hp(alive), Is.EqualTo(max));
        Assert.That(Hp(dead), Is.EqualTo(deadHp));
    }

    [Test]
    public void HpRisesInDiscreteTicksAndAHealAddsAnExtraLanding()
    {
        boss.Cast(Pulsar);
        Advance(2.6f);
        var victim = Members.First(m => Hp(m) < m.Proxy!.MaxHealth);
        var previous = Members.ToDictionary(m => m, Hp);
        var rises = new List<(SimCharacter Who, float At)>();
        for (var i = 0; i < 1500; i++)
        {
            if (i == 500) world.Healing.Heal(victim, 0.1f);
            Advance(Step);
            foreach (var m in Members)
            {
                if (Hp(m) > previous[m]) rises.Add((m, now));
                previous[m] = Hp(m);
            }
        }
        Assert.That(rises.Select(r => r.Who).Distinct(), Is.EquivalentTo(Members), "all living members receive natural regeneration");
        Assert.That(rises.Count(r => r.Who == victim), Is.InRange(4, 6), "HP rises on beats, not as a ramp");
    }

    [Test]
    public void AHealLandsAfterTheMembersOwnServerTickAndByTheAmountAsked()
    {
        boss.Cast(Pulsar);
        Advance(2.6f);
        var victim = Members.First(m => Hp(m) < m.Proxy!.MaxHealth);
        var before = Hp(victim);
        var start = now;
        world.Healing.Heal(victim, 0.05f);
        float? at = null;
        while (now - start < 1f)
        {
            Advance(Step);
            if (at == null && Hp(victim) != before) at = now - start;
        }
        Assert.That(at, Is.InRange(0.05f - 2 * Step, 0.35f + 2 * Step));
        Assert.That(Hp(victim) - before, Is.EqualTo((uint)MathF.Round(0.05f * victim.Proxy!.MaxHealth)).Within(2u));
    }

    [Test]
    public void AHealNeverGoesPastFullOrLandsOnTheDead()
    {
        var full = Members.First();
        var max = full.Proxy!.MaxHealth;
        world.Healing.Heal(full, 0.5f);
        Advance(1f);
        Assert.That(Hp(full), Is.EqualTo(max));
        var dead = Members.Last();
        dead.Die(0);
        var health = Hp(dead);
        world.Healing.Heal(dead, 0.5f);
        Advance(1f);
        Assert.That(Hp(dead), Is.EqualTo(health), "the dead stay as they are");
    }

    [Test]
    public void HealsStaggerPerMemberAndLeaveTheRunsRngAlone()
    {
        boss.Cast(Pulsar);
        Advance(2.6f);
        var before = Members.ToDictionary(m => m, Hp);
        var start = now;
        foreach (var m in Members) world.Healing.Heal(m, 0.02f);
        var landed = new Dictionary<SimCharacter, float>();
        while (now - start < 1f)
        {
            Advance(Step);
            foreach (var m in Members)
                if (Hp(m) != before[m] && !landed.ContainsKey(m)) landed[m] = now - start;
        }
        Assert.That(landed.Values.Distinct().Count(), Is.GreaterThan(1), "not all on one beat");
        Assert.That(world.Rng.Next(1_000_000), Is.EqualTo(new Rng(Seed).Next(1_000_000)));
    }
}
