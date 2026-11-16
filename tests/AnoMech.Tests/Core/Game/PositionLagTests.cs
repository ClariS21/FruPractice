using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using AnoMech.Scenarios.Fru;
using static AnoMech.Core.EnemyActions.EnemyActionEffects;

namespace AnoMech.Tests;

public class PositionLagTests
{
    private const uint ChaosP3BNpcBaseId = 19508;
    private const float Step = 0.01f;
    private const float CastSeconds = 1f;

    private static readonly Vector3 Inside = new(0f, 0f, 0f);
    private static readonly Vector3 Outside = new(8f, 0f, 0f);

    private static readonly EnemyAction Circle = FruActions.MornAfah with
    {
        Cast = new() { CastSecondsOverride = CastSeconds },
        Area = new() { CastTypeOverride = CastType.Circle, Size = 5f },
        Effects = [Damage()],
        Timing = new() { DamageDelay = 0.5f },
    };

    private sealed class Run : IDisposable
    {
        public readonly Game Game = new();
        public readonly SimWorld World;
        public readonly SimEnemy Boss;
        public readonly SimPlayer Player;
        public float Now;

        public Run(int seed, int pingMs)
        {
            var fake = FakeGame.Install();
            fake.BattleCharas.Player.ClassJob = (byte)JobId.Sage;
            DalamudServices.Install(nameof(Plugin.Game), Game);
            DalamudServices.Install(nameof(Plugin.Config), new Configuration());
            World = Game.World;
            World.Reseed(seed);
            World.SimulatedPingMs = pingMs;
            World.SeatParty(new LocalPartyProvider().Roster((uint)JobId.Sage));
            Boss = World.SpawnEnemy(new EnemySpawnConfig(ChaosP3BNpcBaseId))!;
            Player = World.Party.AllMembers().OfType<SimPlayer>().Single();
            Player.SetPosition(Inside);
            Advance(3f);
        }

        public void Advance(float seconds)
        {
            for (var left = seconds; left > 0.0001f; left -= Step)
            {
                World.Events.Tick(Step);
                World.Tick(Step);
                Now += Step;
            }
        }

        public void Dispose()
        {
            World.Despawn();
            DalamudServices.Install(nameof(Plugin.Game), null!);
            DalamudServices.Install(nameof(Plugin.Config), null!);
        }
    }

    // Casts a circle on the origin, steps the player out `stepOutBefore` seconds before its snapshot,
    // and says whether the circle caught them.
    private static bool HitWhenSteppingOut(int pingMs, float stepOutBefore, int seed = 1)
    {
        using var run = new Run(seed, pingMs);
        run.Boss.Cast(Circle, Inside);
        run.Advance(CastSeconds - stepOutBefore);
        run.Player.SetPosition(Outside);
        run.Advance(stepOutBefore + 0.05f);
        return run.Player.LastAoe is not null;
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void At200MsAStepOut100MsBeforeTheSnapshotIsStillHit(int seed)
        => Assert.That(HitWhenSteppingOut(200, 0.1f, seed), Is.True);

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void At200MsAStepOut350MsBeforeTheSnapshotIsSafe(int seed)
        => Assert.That(HitWhenSteppingOut(200, 0.35f, seed), Is.False);

    [Test]
    public void AtNoPingBothStepOutsAreSafe()
    {
        Assert.That(HitWhenSteppingOut(0, 0.1f), Is.False);
        Assert.That(HitWhenSteppingOut(0, 0.35f), Is.False);
    }

    [Test]
    public void ALookRoundedAwayFromAGaze100MsBeforeTheSnapshotStillCountsAsLookingAt200Ms()
    {
        var gaze = FruActions.ShiningArmor with
        {
            Cast = new() { CastSecondsOverride = CastSeconds },
            Area = new() { CastTypeOverride = CastType.Circle, Size = 40f },
            Timing = new() { DamageDelay = 0.3f },
        };
        bool Killed(int pingMs)
        {
            using var run = new Run(1, pingMs);
            run.Player.SetPosition(new Vector3(0f, 0f, 8f));
            run.Player.SetRotation(MathF.PI);
            run.Advance(0.2f);
            run.Boss.Cast(gaze);
            run.Advance(CastSeconds - 0.1f);
            run.Player.SetRotation(0f);
            run.Advance(3f);
            return run.Player.Dead;
        }
        Assert.That(Killed(200), Is.True);
        Assert.That(Killed(0), Is.False);
    }

    [Test]
    public void BotsAreNeverLagged()
    {
        using var run = new Run(1, 300);
        var bot = run.World.Party.AllMembers().OfType<SimPartyNpc>().First();
        bot.SetPosition(Inside);
        run.Advance(0.5f);
        bot.SetPosition(Outside);
        run.Advance(0.02f);
        Assert.That(bot.JudgedPosition, Is.EqualTo(bot.Position));
        Assert.That(run.World.Party.Find.InsideCircle(Inside, 5f), Does.Not.Contain(bot));
    }

    [Test]
    public void TheLookbackDoesNotTouchTheMechanicStream()
    {
        using var run = new Run(77, 200);
        var expectedNext = new Rng(77).Next(100000);
        run.Boss.Cast(Circle, Inside);
        run.Advance(CastSeconds + 0.5f);
        Assert.That(run.World.Rng.Next(100000), Is.EqualTo(expectedNext));
    }
}
