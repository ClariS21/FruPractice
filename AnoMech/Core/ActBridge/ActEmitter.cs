using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using SimGame = AnoMech.Core.Game.Game;

namespace AnoMech.Core.ActBridge;

// Writes what the sim hands the game client as the ACT log lines a real pull would have produced.
// ACT cannot see them itself: the sim bypasses the packet path it captures. Installed over Natives
// like a recording layer: every call reaches the real client first, then goes out as lines.
internal sealed class ActEmitter
{
    private const uint SetTetherCategory = 0x23;
    private const uint HeadMarkerCategory = 0x22;
    private const uint CancelCastCategory = 0x0F;
    private const uint DeathAnimationCategory = 0x0E;
    private const uint InterruptedLogMessage = 540;
    private const uint InstanceSecondsRemaining = 0x1C20;

    private readonly IActLineSink sink;
    private readonly Func<bool> enabled;
    private readonly IBattleCharas realCharas;
    private readonly IMapEffects realMap;
    private readonly List<EmittingBattleChara> charas = [];
    private readonly EmittingBattleChara localPlayer;
    private SimGame? game;

    private ActEmitter(IActLineSink sink, Func<bool> enabled)
    {
        this.sink = sink;
        this.enabled = enabled;
        realCharas = Natives.BattleCharas;
        realMap = Natives.MapEffects;
        localPlayer = new EmittingBattleChara(realCharas.LocalPlayer, this, isLocal: true);
        charas.Add(localPlayer);
    }

    public static ActEmitter Install(IActLineSink sink, Func<bool> enabled)
    {
        var emitter = new ActEmitter(sink, enabled);
        Natives.BattleCharas = new EmittingBattleCharas(emitter);
        Natives.MapEffects = new EmittingMapEffects(emitter);
        return emitter;
    }

    public void Uninstall()
    {
        if (game is not null)
        {
            game.RunBeginning -= RunStarted;
            game.RunEnding -= CloseHeldStatuses;
            game.RunEnded -= RunEnded;
            game = null;
        }
        Natives.BattleCharas = realCharas;
        Natives.MapEffects = realMap;
    }

    public void Attach(SimGame game)
    {
        if (this.game is not null) throw new InvalidOperationException("Emitter is already attached to a game.");
        this.game = game;
        game.RunBeginning += RunStarted;
        game.RunEnding += CloseHeldStatuses;
        game.RunEnded += RunEnded;
    }

    // The run's zone: its director is the one retail's 33 lines name.
    private uint? DirectorId => game?.Zone?.DirectorId;

    private void RunStarted() => Guard("33 commence", () =>
    {
        if (Active && DirectorId is { } director) Emit(ActLine.ActorControl(director, 0x40000006, InstanceSecondsRemaining));
    });

    private void CloseHeldStatuses() => Guard("30 run end", () =>
    {
        foreach (var chara in charas)
        {
            var held = chara.Held;
            chara.Held = new();
            if (!Active || held.Count == 0) continue;
            var target = Actor(chara);
            foreach (var (_, slot) in held)
                Emit(ActLine.LosesEffect(slot.StatusId, Natives.Data.StatusName(slot.StatusId), Actor(slot.SourceEntityId), target, slot.Param));
        }
    });

    private void RunEnded() => Guard("33 wipe", () =>
    {
        if (!Active || DirectorId is not { } director) return;
        Emit(ActLine.ActorControl(director, 0x40000005));
        Emit(ActLine.ActorControl(director, 0x4000000F, InstanceSecondsRemaining));
        Emit(ActLine.ActorControl(director, 0x40000011));
        // The first wipe takes cactbot out of combat; the second resets its trigger data.
        Emit(ActLine.ActorControl(director, 0x4000000F, InstanceSecondsRemaining));
    });

    private bool Active => sink.Connected && enabled() && (game is null || game.World.Calibration.ActEmission);

    private void Emit(string line) => sink.Send(line);

    private readonly HashSet<string> warned = [];

    // Emission runs inside the sim's own native calls, which it must never break: nothing escapes,
    // and a failure that keeps recurring is logged once per line type.
    private void Guard(string kind, Action emit)
    {
        try
        {
            emit();
        }
        catch (Exception e)
        {
            try
            {
                if (warned.Add(kind)) DiagnosticLog.Warn($"[ActBridge] Emitting {kind} threw and was skipped: {e}");
            }
            catch (Exception)
            {
            }
        }
    }

    // ── Actors ───────────────────────────────────────────────────────────────

    private static ActLineActor Actor(IBattleCharaProxy chara)
        => new(chara.EntityId, chara.Name, chara.Health, chara.MaxHealth, chara.Mana, chara.MaxMana, chara.Position, chara.Rotation);

    private ActLineActor Actor(uint entityId)
    {
        if (entityId is 0 or ActLine.NoActor) return ActLineActor.None;
        foreach (var chara in charas)
            if (chara.Exists && chara.EntityId == entityId) return Actor(chara);
        return new ActLineActor(entityId, "");
    }

    private ActLineActor Actor(GameObjectId? id) => Actor(id?.ObjectId ?? 0);

    // ── Events ───────────────────────────────────────────────────────────────

    private void Cast(IBattleCharaProxy chara, ActorCastData cast) => Guard("20", () => CastLines(chara, cast));

    private void CastLines(IBattleCharaProxy chara, ActorCastData cast)
    {
        if (!Active || !chara.Exists) return;
        var source = Actor(chara);
        Emit(ActLine.StartsUsing(source, cast.ActionId, Natives.Data.ActionName(cast.ActionId), Actor(cast.Target), cast.CastTime));
        Emit(ActLine.StartsUsingExtra(source.Id, cast.ActionId, cast.Position, cast.Rotation));
    }

    private void Effect(IBattleCharaProxy chara, ActionEffectData effect, uint sequence) => Guard("21", () => EffectLines(chara, effect, sequence));

    private void EffectLines(IBattleCharaProxy chara, ActionEffectData effect, uint sequence)
    {
        if (!Active || !chara.Exists) return;
        var source = Actor(chara);
        var ability = Natives.Data.ActionName(effect.ActionId);
        var targets = effect.Targets is { Count: > 0 } list ? list : null;
        var count = targets?.Count ?? 1;
        for (var i = 0; i < count; i++)
        {
            var target = targets is null ? ActLineActor.None : Actor(targets[i].Id);
            Emit(ActLine.ActionEffect(count > 1, source, effect.ActionId, ability, target, targets?[i].Effects, sequence, i, count,
                (uint)effect.ActionType, effect.SpellId, effect.AnimationLock, MathUtil.QuantizeRotation(effect.Rotation)));
        }
        Emit(ActLine.AbilityExtra(source.Id, effect.ActionId, sequence, count > 1 ? effect.Position : null,
            MathUtil.NormalizeRotation(effect.Rotation), effect.AnimationTarget?.ObjectId ?? 0));
    }

    private void Control(EmittingBattleChara chara, uint category, uint[] args) => Guard($"ActorControl 0x{category:X}", () => ControlLines(chara, category, args));

    private void ControlLines(EmittingBattleChara chara, uint category, uint[] args)
    {
        if (!Active || !chara.Exists) return;
        var actor = Actor(chara);
        switch (category)
        {
            case HeadMarkerCategory:
                Emit(ActLine.HeadMarker(actor, args[0]));
                break;
            case SetTetherCategory:
                Emit(ActLine.Tether(actor, Actor(args[2]), args[1]));
                break;
            case CancelCastCategory:
                Emit(ActLine.CancelAbility(actor, args[2], Natives.Data.ActionName(args[2]), args[0] == InterruptedLogMessage ? "Interrupted" : "Cancelled"));
                break;
            case DeathAnimationCategory:
                Emit(ActLine.WasDefeated(actor, ActLineActor.None));
                break;
        }
        if (!chara.IsLocal) Emit(ActLine.ActorControlExtra(actor.Id, category, args[0], args[1], args[2], args[3]));
    }

    private void Moved(IBattleCharaProxy chara, Vector3 position, float rotation) => Guard("271", () => MovedLine(chara, position, rotation));

    private void MovedLine(IBattleCharaProxy chara, Vector3 position, float rotation)
    {
        if (Active && chara.Exists) Emit(ActLine.ActorSetPos(chara.EntityId, position, rotation));
    }

    // One 26 per status that appeared and one 30 per status that left, not one per refresh of the
    // full list; the same status from the same source taken again with new stacks is a new 26.
    // The held set is kept current while nothing listens, so connecting mid-run doesn't dump it.
    private void Statuses(EmittingBattleChara chara, IReadOnlyList<StatusSlot> statuses) => Guard("26", () => StatusLines(chara, statuses));

    private void StatusLines(EmittingBattleChara chara, IReadOnlyList<StatusSlot> statuses)
    {
        var now = new Dictionary<(ushort Id, uint Source, int Nth), StatusSlot>();
        var seen = new Dictionary<(ushort, uint), int>();
        foreach (var slot in statuses)
        {
            if (slot.StatusId == 0) continue;
            var key = (slot.StatusId, slot.SourceEntityId);
            var nth = seen.GetValueOrDefault(key);
            seen[key] = nth + 1;
            now[(slot.StatusId, slot.SourceEntityId, nth)] = slot;
        }

        var before = chara.Held;
        chara.Held = now;
        if (!Active || !chara.Exists) return;

        var target = Actor(chara);
        foreach (var (key, slot) in now)
        {
            if (before.TryGetValue(key, out var old) && old.Param == slot.Param) continue;
            Emit(ActLine.GainsEffect(slot.StatusId, Natives.Data.StatusName(slot.StatusId), slot.RemainingTime, Actor(slot.SourceEntityId), target, slot.Param));
        }
        foreach (var (key, slot) in before)
            if (!now.ContainsKey(key))
                Emit(ActLine.LosesEffect(slot.StatusId, Natives.Data.StatusName(slot.StatusId), Actor(slot.SourceEntityId), target, slot.Param));
    }

    private void Spawned(uint entityId, EnemySpawnConfig config, Placement placement) => Guard("272", () => SpawnedLines(entityId, config, placement));

    // The sim's spawn packets carry no animation state or parent.
    private void SpawnedLines(uint entityId, EnemySpawnConfig config, Placement placement)
    {
        if (!Active) return;
        Emit(ActLine.SpawnNpcExtra(entityId, ActLine.NoActor, config.TetherId, 0));
        Emit(ActLine.ActorSetPos(entityId, placement.Position, placement.Rotation));
    }

    private EmittingBattleChara Wrap(IBattleCharaProxy chara)
    {
        var wrapped = new EmittingBattleChara(chara, this, isLocal: false);
        charas.Add(wrapped);
        return wrapped;
    }

    // ── The emitting natives ─────────────────────────────────────────────────

    private sealed class EmittingBattleCharas(ActEmitter emitter) : IBattleCharas
    {
        public IBattleCharaProxy LocalPlayer => emitter.localPlayer;

        public IBattleCharaProxy? SpawnBattleNpcFromPacket(EnemySpawnConfig config, Placement placement, out uint entityId)
        {
            if (emitter.realCharas.SpawnBattleNpcFromPacket(config, placement, out entityId) is not { } chara) return null;
            emitter.Spawned(entityId, config, placement);
            return emitter.Wrap(chara);
        }

        public IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, BotLook? look, BotWeaponSet? weapons, byte level, uint maxHealth, Placement placement, out uint entityId)
            => emitter.realCharas.SpawnDoppel(preset, look, weapons, level, maxHealth, placement, out entityId) is { } chara ? emitter.Wrap(chara) : null;

        public IReadOnlyList<BotLook> LoadMainTankLooks() => emitter.realCharas.LoadMainTankLooks();

        public bool IsInCharacterManager(uint entityId) => emitter.realCharas.IsInCharacterManager(entityId);

        public void ReleaseSlot(int slot) => emitter.realCharas.ReleaseSlot(slot);
    }

    private sealed class EmittingBattleChara(IBattleCharaProxy inner, ActEmitter emitter, bool isLocal) : IBattleCharaProxy
    {
        public bool IsLocal => isLocal;

        public Dictionary<(ushort Id, uint Source, int Nth), StatusSlot> Held { get; set; } = new();

        public bool Exists => inner.Exists;
        public int Slot => inner.Slot;
        public uint EntityId => inner.EntityId;
        public GameObjectId GameObjectId => inner.GameObjectId;
        public string Name => inner.Name;
        public byte ClassJob => inner.ClassJob;
        public Vector3 Position => inner.Position;
        public float Rotation => inner.Rotation;
        public float HitboxRadius => inner.HitboxRadius;
        public bool HasDrawObject => inner.HasDrawObject;
        public bool IsDrawObjectVisible => inner.IsDrawObjectVisible;
        public byte ModelState => inner.ModelState;
        public bool IsCasting => inner.IsCasting;
        public uint CastActionId => inner.CastActionId;
        public float CurrentCastTime => inner.CurrentCastTime;
        public float TotalCastTime => inner.TotalCastTime;
        public uint Health => inner.Health;
        public uint MaxHealth => inner.MaxHealth;
        public uint Mana => inner.Mana;
        public uint MaxMana => inner.MaxMana;
        public byte TargetableStatus => inner.TargetableStatus;
        public ushort GetTetherId(byte slot) => inner.GetTetherId(slot);
        public string DescribeDrawState() => inner.DescribeDrawState();
        public string DescribeActionTimeline() => inner.DescribeActionTimeline();
        public string DescribeTimelineSlotIds() => inner.DescribeTimelineSlotIds();
        public string DescribeModelSlots() => inner.DescribeModelSlots();

        public void SetPos(Vector3 position, float rotation)
        {
            inner.SetPos(position, rotation);
            emitter.Moved(this, position, rotation);
        }

        public void Move(Vector3 position, float rotation, ActorMoveFlags flags, float speed) => inner.Move(position, rotation, flags, speed);

        public void NpcKnockback(Vector3 position, float rotation, float durationSeconds) => inner.NpcKnockback(position, rotation, durationSeconds);

        public void ReceiveActorCast(ActorCastData cast)
        {
            inner.ReceiveActorCast(cast);
            emitter.Cast(this, cast);
        }

        public uint ReceiveActionEffect(ActionEffectData effect)
        {
            var sequence = inner.ReceiveActionEffect(effect);
            emitter.Effect(this, effect, sequence);
            return sequence;
        }

        public void ReceiveEffectResult(uint globalSequence, byte targetIndex, uint health) => inner.ReceiveEffectResult(globalSequence, targetIndex, health);

        public void ActorControl(uint category, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0, uint arg5 = 0, uint arg6 = 0, uint arg7 = 0, uint arg8 = 0)
        {
            inner.ActorControl(category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);
            emitter.Control(this, category, [arg1, arg2, arg3, arg4]);
        }

        public void ActorControlTarget(uint category, GameObjectId target, uint arg1 = 0, uint arg2 = 0, uint arg3 = 0, uint arg4 = 0)
            => inner.ActorControlTarget(category, target, arg1, arg2, arg3, arg4);

        public void ForcedFollow(uint? targetEntityId) => inner.ForcedFollow(targetEntityId);

        public void StopForcedMove() => inner.StopForcedMove();

        public void ReceiveStatusList(IReadOnlyList<StatusSlot> statuses, uint health, uint mana, byte shield)
        {
            inner.ReceiveStatusList(statuses, health, mana, shield);
            emitter.Statuses(this, statuses);
        }

        public void Despawn()
        {
            inner.Despawn();
            if (!isLocal) emitter.charas.Remove(this);
        }
    }

    private sealed class EmittingMapEffects(ActEmitter emitter) : IMapEffects
    {
        public bool Loaded
        {
            get => emitter.realMap.Loaded;
            set => emitter.realMap.Loaded = value;
        }

        public bool Apply(uint packetFlags, byte index)
        {
            if (!emitter.realMap.Apply(packetFlags, index)) return false;
            emitter.Guard("257", () =>
            {
                if (emitter.Active) emitter.Emit(ActLine.MapEffect(packetFlags, index));
            });
            return true;
        }

        public bool ApplySnapshot(ReadOnlySpan<ushort> states, bool zoneIn) => emitter.realMap.ApplySnapshot(states, zoneIn);

        public void LogAllSlots(string label) => emitter.realMap.LogAllSlots(label);

        public void Dispose() => emitter.realMap.Dispose();
    }
}
