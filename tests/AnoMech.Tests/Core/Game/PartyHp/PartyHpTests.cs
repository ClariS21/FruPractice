using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;

namespace AnoMech.Tests;

public class PartyHpTests
{
    private const uint ChaosP3BNpcBaseId = 19508;
    private const float Step = 0.01f;
    private const float CastSeconds = 1f;
    private const float DamageDelay = 1f;
    private const int Seed = 1234;

    // Damage lands at CastSeconds + DamageDelay after the cast starts; each member's HP follows.
    private static readonly EnemyAction Pulsar = FruActions.ShockwavePulsar with
    {
        Cast = new() { CastSecondsOverride = CastSeconds },
        Effects = [Damage()],
        Timing = new() { DamageDelay = DamageDelay },
    };

    private sealed class Run : IDisposable
    {
        public readonly Game Game = new();
        public readonly SimWorld World;
        public readonly SimEnemy Boss;
        public float Now;

        public Run(int seed)
        {
            var fake = FakeGame.Install();
            fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
            DalamudServices.Install(nameof(Plugin.Game), Game);
            World = Game.World;
            World.Reseed(seed);
            World.BotCombatEnabled = false;
            World.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
            Boss = World.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
            Advance(0.5f);
        }

        public SimCharacter Member(PartyRole role) => World.Party.Get(role)!;
        public IEnumerable<SimCharacter> Members => Enum.GetValues<PartyRole>().Select(Member);
        public uint Hp(SimCharacter who) => who.Proxy!.Health;
        public uint MaxHp(SimCharacter who) => who.Proxy!.MaxHealth;

        public void Advance(float seconds)
        {
            for (var left = seconds; left > 0f; left -= Step)
                Tick();
        }

        public void Tick()
        {
            World.Events.Tick(Step);
            World.Tick(Step);
            Now += Step;
        }

        public void Dispose()
        {
            World.Despawn();
            DalamudServices.Install(nameof(Plugin.Game), null!);
        }
    }

    [Test]
    public void BotsSpawnWithTheirJobsMaxHpWithinThreePercent()
    {
        using var run = new Run(Seed);
        foreach (var member in run.Members.OfType<SimPartyNpc>())
        {
            var expected = member.ClassJob switch
            {
                19 or 21 or 32 or 37 => 233_000f,
                24 => 145_400f,
                28 => 143_600f,
                22 => 163_200f,
                20 => 158_300f,
                23 => 161_000f,
                25 => 147_000f,
                _ => 0f,
            };
            Assert.That(expected, Is.Not.Zero, $"job {member.ClassJob} has a table entry in this test");
            Assert.That(run.MaxHp(member), Is.EqualTo(expected).Within(expected * 0.03f + 1f), $"{member.Role}");
        }
    }

    [TestCase(PartyRole.MainTank, 0.15f)]
    [TestCase(PartyRole.RegenHealer, 0.46f)]
    [TestCase(PartyRole.MeleeDpsA, 0.53f)]
    public void AKnownAbilityTakesItsTableFractionOfMaxHp(PartyRole role, float fraction)
    {
        using var run = new Run(Seed);
        var who = run.Member(role);
        var max = run.MaxHp(who);
        run.Boss.Cast(Pulsar);
        run.Advance(CastSeconds + DamageDelay + 1f);
        var lost = (float)(max - run.Hp(who));
        Assert.That(lost / max, Is.EqualTo(fraction).Within(fraction * 0.11f), "fraction +/-10%");
        Assert.That(run.Hp(who), Is.GreaterThanOrEqualTo(1u));
    }

    [Test]
    public void EachMembersHpUpdateLandsAfterItsOwnServerTick()
    {
        var first = HpDropTimes(Seed);
        Assert.That(first, Has.Count.EqualTo(8));
        var confirmAt = CastSeconds + DamageDelay;
        foreach (var (role, at) in first)
            Assert.That(at, Is.InRange(confirmAt + 0.05f - 2 * Step, confirmAt + 0.35f + 2 * Step), $"{role}");
        Assert.That(first.Values.Distinct().Count(), Is.GreaterThan(1), "not all at one moment");
        Assert.That(HpDropTimes(Seed), Is.EqualTo(first), "same seed, same moments");
    }

    private static Dictionary<PartyRole, float> HpDropTimes(int seed)
    {
        using var run = new Run(seed);
        var full = run.Members.ToDictionary(m => m, run.Hp);
        var start = run.Now;
        var drops = new Dictionary<PartyRole, float>();
        run.Boss.Cast(Pulsar);
        while (run.Now - start < CastSeconds + DamageDelay + 1f)
        {
            run.Tick();
            foreach (var m in run.Members)
                if (run.Hp(m) < full[m] && !drops.ContainsKey(((ISimPartyMember)m).Role))
                    drops[((ISimPartyMember)m).Role] = MathF.Round(run.Now - start, 2);
        }
        return drops;
    }

    [Test]
    public void ADyingTargetDiesAtOrAfterItsHpUpdate()
    {
        using var run = new Run(Seed);
        var victim = run.Member(PartyRole.MeleeDpsA);
        var lethal = Pulsar with { Effects = [Damage(Severity.Lethal)] };
        var start = run.Now;
        float? deadAt = null;
        float? updatedAt = null;
        var before = run.Hp(victim);
        run.Boss.Cast(lethal);
        while (run.Now - start < 4f)
        {
            run.Tick();
            if (updatedAt is null && run.Hp(victim) != before) updatedAt = run.Now - start;
            if (deadAt is null && victim.Dead) deadAt = run.Now - start;
        }
        Assert.That(deadAt, Is.Not.Null);
        Assert.That(updatedAt ?? deadAt, Is.LessThanOrEqualTo(deadAt!.Value + Step));
        Assert.That(deadAt, Is.GreaterThanOrEqualTo(CastSeconds + DamageDelay + 0.05f - 2 * Step));
    }

    [Test]
    public void HellsJudgmentLeavesEveryoneAtOneHpBeforeNaturalRegeneration()
    {
        using var run = new Run(Seed);
        var action = FruActions.HellsJudgment with { Cast = new() { CastSecondsOverride = CastSeconds }, Timing = new() { DamageDelay = DamageDelay } };
        run.Boss.Cast(action);
        run.Advance(CastSeconds + DamageDelay + 1f);
        Assert.That(run.Members.Select(run.Hp), Has.All.EqualTo(1u), "everyone is left at 1 HP");
        run.Advance(15f);
        Assert.That(run.Members.Select(run.Hp), Has.All.GreaterThan(1u), "natural regeneration restores HP on later server beats");
    }

    [Test]
    public void TheRunsRngIsUntouchedByHp()
    {
        using var run = new Run(Seed);
        run.Boss.Cast(Pulsar);
        run.Advance(6f);
        Assert.That(run.World.Rng.Next(1_000_000), Is.EqualTo(new Rng(Seed).Next(1_000_000)));
    }
}
