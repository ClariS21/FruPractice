using System.Numerics;
using AnoMech.Core.ActBridge;
using AnoMech.Core.Game;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Tests;

public class ActEmitterTests
{
    private const uint OracleBNpcBaseId = 17831;
    private const uint ActionId = 0x9D4A;

    private sealed class RecordingSink : IActLineSink
    {
        public bool Connected { get; set; } = true;
        public List<string> Lines { get; } = [];
        public void Send(string line) => Lines.Add(line);
    }

    private RecordingSink sink = null!;
    private ActEmitter emitter = null!;
    private bool enabled;
    private IBattleCharaProxy boss = null!;
    private IBattleCharaProxy player = null!;

    [SetUp]
    public void Install()
    {
        FakeGame.Install();
        sink = new RecordingSink();
        enabled = true;
        emitter = ActEmitter.Install(sink, () => enabled);
        boss = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId), new Placement(new Vector3(100f, 0f, 90f), 1.5f), out _)!;
        player = Natives.BattleCharas.LocalPlayer;
        sink.Lines.Clear();
    }

    [TearDown]
    public void Uninstall() => emitter.Uninstall();

    private static string Ability => Natives.Data.ActionName(ActionId);

    [Test]
    public void CastStartIsOneStartsUsingLine()
    {
        boss.ReceiveActorCast(new ActorCastData(ActionId, ActionType.Action, 4.7f, 0f, false, 1.5f, new Vector3(100f, 0f, 90f)));

        Assert.That(sink.Lines, Has.Count.EqualTo(2));
        Assert.That(sink.Lines[0], Is.EqualTo($"20|{boss.EntityId:X8}|{boss.Name}|9D4A|{Ability}|E0000000||4.70|100.00|0.00|90.00|1.50"));
        Assert.That(sink.Lines[1], Is.EqualTo($"263|{boss.EntityId:X8}|9D4A|100.00|0.00|90.00|1.50"));
    }

    [Test]
    public void CastAtATargetNamesIt()
    {
        boss.ReceiveActorCast(new ActorCastData(ActionId, ActionType.Action, 4.7f, 0f, false, 0f, default, Target: player.GameObjectId));

        Assert.That(sink.Lines[0], Does.Contain($"|{Ability}|{player.EntityId:X8}|{player.Name}|4.70|"));
    }

    [Test]
    public void ActionEffectOnOneTargetIsOneType21Line()
    {
        boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, 0f, default,
            Targets: [new ActionEffectTarget(player.GameObjectId, [ActionEffectEntry.Damage(1000, 5)])]));

        // Then its 264.
        Assert.That(sink.Lines, Has.Count.EqualTo(2));
        Assert.That(sink.Lines[0], Does.StartWith($"21|{boss.EntityId:X8}|{boss.Name}|9D4A|{Ability}|{player.EntityId:X8}|{player.Name}|750003|E8030000|"));
    }

    [Test]
    public void ActionEffectOnOneTargetEndsWithAnAbilityExtraWithoutALocation()
    {
        var sequence = boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, 1.5f, new Vector3(100f, 0f, 90f),
            AnimationTarget: player.GameObjectId, Targets: [new ActionEffectTarget(player.GameObjectId)]));

        Assert.That(sink.Lines[^1], Is.EqualTo($"264|{boss.EntityId:X8}|9D4A|{sequence:X8}|0||||1.500|{player.EntityId:X8}"));
    }

    [Test]
    public void ActionEffectOnSeveralTargetsEndsWithAnAbilityExtraAtItsLocation()
    {
        var other = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId), new Placement(), out _)!;
        sink.Lines.Clear();
        var sequence = boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, -1.5f, new Vector3(100f, 0f, 90f),
            Targets: [new ActionEffectTarget(player.GameObjectId), new ActionEffectTarget(other.GameObjectId)]));

        Assert.That(sink.Lines, Has.Count.EqualTo(3));
        Assert.That(sink.Lines[2], Is.EqualTo($"264|{boss.EntityId:X8}|9D4A|{sequence:X8}|1|100.000|0.000|90.000|-1.500|E0000000"));
    }

    [Test]
    public void ActionEffectOnSeveralTargetsIsOneType22LinePerTarget()
    {
        var other = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId), new Placement(), out _)!;
        sink.Lines.Clear();
        boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, 0f, default,
            Targets: [new ActionEffectTarget(player.GameObjectId), new ActionEffectTarget(other.GameObjectId)]));

        // Then the packet's one 264.
        Assert.That(sink.Lines, Has.Count.EqualTo(3));
        Assert.That(sink.Lines[0], Does.StartWith("22|"));
        Assert.That(sink.Lines[1], Does.Contain($"|{other.EntityId:X8}|{other.Name}|"));
        Assert.That(sink.Lines[0], Does.Contain("|0|2|||"));
        Assert.That(sink.Lines[1], Does.Contain("|1|2|||"));
    }

    [Test]
    public void ActionEffectWithoutTargetsIsOneLineAtNoTarget()
    {
        boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, 0f, default));

        // Then its 264.
        Assert.That(sink.Lines, Has.Count.EqualTo(2));
        Assert.That(sink.Lines[0], Does.Contain($"|{Ability}|E0000000||0|0|"));
    }

    [Test]
    public void StatusAddedThenRemovedIsOneGainAndOneLoss()
    {
        var slot = new StatusSlot(2091, 0, 20f, boss.EntityId);
        player.ReceiveStatusList([slot], 1, 1, 0);
        player.ReceiveStatusList([slot with { RemainingTime = 19f }], 1, 1, 0);
        player.ReceiveStatusList([], 1, 1, 0);
        player.ReceiveStatusList([], 1, 1, 0);

        var status = Natives.Data.StatusName(2091);
        Assert.That(sink.Lines, Has.Count.EqualTo(2));
        Assert.That(sink.Lines[0], Does.StartWith($"26|82B|{status}|20.00|{boss.EntityId:X8}|{boss.Name}|{player.EntityId:X8}|{player.Name}|00|"));
        Assert.That(sink.Lines[1], Does.StartWith($"30|82B|{status}|0.00|{boss.EntityId:X8}|{boss.Name}|{player.EntityId:X8}|{player.Name}|00|"));
    }

    [Test]
    public void ChangedStacksAreAnotherGain()
    {
        player.ReceiveStatusList([new StatusSlot(2091, 1, 20f, 0)], 1, 1, 0);
        player.ReceiveStatusList([new StatusSlot(2091, 2, 20f, 0)], 1, 1, 0);

        Assert.That(sink.Lines, Has.Count.EqualTo(2));
        Assert.That(sink.Lines[1], Does.Contain("|02|"));
    }

    [Test]
    public void StatusesHeldBeforeConnectingAreNotDumped()
    {
        sink.Connected = false;
        player.ReceiveStatusList([new StatusSlot(2091, 0, 20f, 0)], 1, 1, 0);
        sink.Connected = true;
        player.ReceiveStatusList([new StatusSlot(2091, 0, 19f, 0)], 1, 1, 0);

        Assert.That(sink.Lines, Is.Empty);
    }

    [Test]
    public void HeadMarkerNamesTheMarkedActor()
    {
        player.ActorControl(0x22, 0x17, player.EntityId);

        Assert.That(sink.Lines, Is.EqualTo(new[] { $"27|{player.EntityId:X8}|{player.Name}|0000|0000|0017|0000|0000|0000" }));
    }

    [Test]
    public void TetherNamesBothEnds()
    {
        boss.ActorControl(0x23, 0, 0x85, player.EntityId, 15);

        Assert.That(sink.Lines[0], Is.EqualTo($"35|{boss.EntityId:X8}|{boss.Name}|{player.EntityId:X8}|{player.Name}|0000|0000|0085|{player.EntityId:X8}|000F|0000"));
    }

    [Test]
    public void CancelledCast()
    {
        boss.ActorControl(0x0F, 537, 1, ActionId, 0);

        Assert.That(sink.Lines[0], Is.EqualTo($"23|{boss.EntityId:X8}|{boss.Name}|9D4A|{Ability}|Cancelled"));
    }

    [Test]
    public void DeathAnimationIsAWasDefeatedLine()
    {
        player.ActorControl(0x0E);

        Assert.That(sink.Lines, Is.EqualTo(new[] { $"25|{player.EntityId:X8}|{player.Name}|E0000000|" }));
    }

    [Test]
    public void ActorControlOnAnNpcIsAlsoAnExtraLine()
    {
        boss.ActorControl(0x3F, 4);

        Assert.That(sink.Lines, Is.EqualTo(new[] { $"273|{boss.EntityId:X8}|003F|4|0|0|0" }));
    }

    [Test]
    public void SetPosIsAnActorSetPosLine()
    {
        boss.SetPos(new Vector3(100f, 0f, 95f), 1.57f);

        Assert.That(sink.Lines, Is.EqualTo(new[] { $"271|{boss.EntityId:X8}|1.57|00|00|100.00|0.00|95.00" }));
    }

    [Test]
    public void SpawnIsASpawnNpcExtraThenAnActorSetPosLine()
    {
        var spawned = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId), new Placement(new Vector3(1f, 0f, 2f), 0.5f), out var id)!;

        Assert.That(sink.Lines, Is.EqualTo(new[] { $"272|{id:X8}|E0000000|0000|00", $"271|{id:X8}|0.50|00|00|1.00|0.00|2.00" }));
        Assert.That(spawned.EntityId, Is.EqualTo(id));
    }

    [Test]
    public void ATetherInTheSpawnIsInItsSpawnNpcExtraAndNoTetherLine()
    {
        var spawned = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId, TetherId: 0x54, TetherTargetId: player.EntityId),
            new Placement(), out var id)!;

        Assert.That(sink.Lines[0], Is.EqualTo($"272|{id:X8}|E0000000|0054|00"));
        Assert.That(sink.Lines, Has.None.StartsWith("35|"));
        Assert.That(spawned.GetTetherId(0), Is.EqualTo(0x54));
    }

    [Test]
    public void AppliedMapEffectIsA257Line()
    {
        Natives.MapEffects.Loaded = true;
        Natives.MapEffects.Apply(0x00020001, 8);

        Assert.That(sink.Lines, Is.EqualTo(new[] { "257|00000000|00020001|08|0000|0000" }));
    }

    [Test]
    public void RefusedMapEffectIsNotSent()
    {
        Natives.MapEffects.Loaded = false;
        Natives.MapEffects.Apply(0x00020001, 8);

        Assert.That(sink.Lines, Is.Empty);
    }

    [Test]
    public void NothingIsSentWhenDisabledOrDisconnected()
    {
        enabled = false;
        boss.ActorControl(0x3F, 4);
        enabled = true;
        sink.Connected = false;
        boss.ActorControl(0x3F, 4);

        Assert.That(sink.Lines, Is.Empty);
    }

    private sealed class ThrowingNames(IGameData inner) : IGameData
    {
        // Only the emitter's own lookups fail; the fake client under it reads the same sheets.
        private static void FailInEmitter()
        {
            if (new System.Diagnostics.StackTrace().ToString().Contains("ActEmitter.") && new System.Diagnostics.StackTrace().ToString().Contains("Line"))
                throw new InvalidOperationException("names");
        }

        public ActionRow? Action(uint actionId) { FailInEmitter(); return inner.Action(actionId); }
        public StatusRow? Status(ushort statusId) { FailInEmitter(); return inner.Status(statusId); }
        public KnockbackRow? Knockback(uint knockbackId) => inner.Knockback(knockbackId);
        public ClassJobRow? ClassJob(uint classJobId) => inner.ClassJob(classJobId);
        public ItemRow? Item(uint itemId) => inner.Item(itemId);
        public BNpcBaseRow? BNpcBase(uint bnpcBaseId) => inner.BNpcBase(bnpcBaseId);
        public ModelCharaRow? ModelChara(uint modelCharaId) => inner.ModelChara(modelCharaId);
        public ModelSkeletonRow? ModelSkeleton(uint skeletonId) => inner.ModelSkeleton(skeletonId);
        public TerritoryTypeRow? TerritoryType(uint territoryId) => inner.TerritoryType(territoryId);
        public ContentFinderConditionRow? ContentFinderCondition(uint id) => inner.ContentFinderCondition(id);
        public string? BNpcName(uint nameId) => inner.BNpcName(nameId);
        public bool WeatherExists(byte weatherId) => inner.WeatherExists(weatherId);
        public bool FileExists(string path) => inner.FileExists(path);
    }

    [Test]
    public void AFailingEmissionNeverReachesTheSim()
    {
        var real = Natives.Data;
        Natives.Data = new ThrowingNames(real);
        try
        {
            Assert.DoesNotThrow(() => boss.ReceiveActorCast(new ActorCastData(ActionId, ActionType.Action, 4.7f, 0f, false, 0f, default)));
            Assert.DoesNotThrow(() => boss.ReceiveActionEffect(new ActionEffectData(ActionId, ActionType.Action, 0.6f, 0, 0, 0, 0f, default)));
            Assert.DoesNotThrow(() => player.ReceiveStatusList([new StatusSlot(2091, 0, 20f, 0)], 1, 1, 0));
            Assert.That(sink.Lines, Is.Empty);
            Assert.That(boss.IsCasting, Is.True);
        }
        finally
        {
            Natives.Data = real;
        }
    }

    [Test]
    public void ASinkThatThrowsNeverReachesTheSim()
    {
        var thrower = new ThrowingSink();
        emitter.Uninstall();
        emitter = ActEmitter.Install(thrower, () => true);
        var chara = Natives.BattleCharas.SpawnBattleNpcFromPacket(new EnemySpawnConfig(OracleBNpcBaseId), new Placement(), out _)!;

        Assert.DoesNotThrow(() => chara.ActorControl(0x3F, 4));
    }

    private sealed class ThrowingSink : IActLineSink
    {
        public bool Connected => true;
        public void Send(string line) => throw new InvalidOperationException("sink");
    }

    [Test]
    public void UninstallRestoresTheRealNatives()
    {
        emitter.Uninstall();

        Assert.That(Natives.BattleCharas.GetType().Name, Is.EqualTo(nameof(FakeBattleCharas)));
        Assert.That(Natives.MapEffects.GetType().Name, Is.EqualTo(nameof(FakeMapEffects)));
        emitter = ActEmitter.Install(sink, () => enabled);
    }
}
