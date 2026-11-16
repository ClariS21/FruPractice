using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Geometry;
using AnoMech.Core.Game.Motion;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.UserActions;
using AnoMech.Scenarios;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.SimObjects;

public sealed class SimPartyNpc : SimNpc, ISimPartyMember
{
    private const float PressHoldSeconds = 1.5f;
    private const float LimitBreakHoldSeconds = 6f;

    public PartyRole Role { get; set; }
    public byte ClassJob { get; }
    public string DisplayName { get; }

    private const float DashSeconds = 0.4f;
    private const float DashHoldSeconds = 0.8f;
    private const float StopShortOfTarget = 2.5f;
    private const float ApproachMargin = 3f;

    private readonly Func<Rng> actionRng;
    private readonly EventScheduler events;
    private readonly Func<Vector3, bool> isOutsideArena;
    private float combatHold;
    private float gapCloserReadyAt;

    internal SimPartyNpc(IBattleCharaProxy proxy, Coordinates coordinates, PartyRole role, byte classJob, string name, ObstacleField obstacles, Func<Rng> actionRng, EventScheduler events, Func<Vector3, bool> isOutsideArena)
        : base(proxy, coordinates,
            self => new SimulatedMotion(() => self.Proxy, coordinates, self.ActorControl, obstacles, turnSpeed: null, npc: false))
    {
        Role = role;
        ClassJob = classJob;
        DisplayName = name;
        this.actionRng = actionRng;
        this.events = events;
        this.isOutsideArena = isOutsideArena;
    }

    // True while a press of the bot's own (mitigation, limit break) plays out and combat must not talk over it.
    internal bool CombatHeld => combatHold > 0f;

    public override void Tick(float deltaSeconds)
    {
        base.Tick(deltaSeconds);
        if (combatHold > 0f) combatHold -= deltaSeconds;
    }

    // The bar of a hard-cast attack on `target`; the caller sends the release when it fills.
    internal void BeginCast(uint actionId, float seconds, SimCharacter target)
        => Casting.NativeCast(actionId, ActionType.Action, 0f, seconds, interruptible: false, targetId: target.GameObjectId);

    // A heal's animation toward the party, carrying each target's heal so the client shows it once the
    // target's EffectResult lands. Returns the sequence those name; the entries' order is the index.
    internal uint PlayHeal(uint actionId, IReadOnlyList<(SimCharacter Who, uint Amount)> heals, SimCharacter animationTarget)
        => Casting.NativeActionEffect(actionId, 0.6f, (ushort)actionId, 0, ActionType.Action, 0,
            animationTargetId: animationTarget.GameObjectId,
            targets: [.. heals.Select(h => new ActionEffectTarget(h.Who.GameObjectId, [ActionEffectEntry.Heal(h.Amount), ActionEffectEntry.ActionMarker(actionId)]))]);

    internal void CancelCast(CastCancelReason reason)
    {
        if (Casting.IsCasting) Casting.Cancel(reason);
    }

    // Plays the job's gap closer at `target` and dashes to its melee range, kept inside the arena.
    // Reach is measured to the target's edge. Further than that, unless `sprintInWhenFar` is off (the
    // dash then just goes as far as it reaches), the bot sprints in first and dashes once close. False when
    // the job has none, it is on cooldown, the bot is down or already in range.
    public bool GapClose(SimCharacter target, bool sprintInWhenFar = true)
    {
        var actionId = BotGapClosers.For(ClassJob);
        if (actionId == 0 || !this.IsAlive() || ActionsLocked || events.Elapsed < gapCloserReadyAt) return false;
        var away = new Vector3(target.Position.X - Position.X, 0f, target.Position.Z - Position.Z);
        var distance = away.Length();
        var stop = target.HitboxRadius + StopShortOfTarget;
        if (distance <= stop) return false;
        var direction = away / distance;
        if (sprintInWhenFar && distance - target.HitboxRadius > BotGapClosers.Range)
        {
            var approach = Position + direction * (distance - target.HitboxRadius - BotGapClosers.Range + ApproachMargin);
            UseSprint(distance / AiSprintSpeed + 1f);
            MoveTo(approach, speed: AiSprintSpeed);
            events.Add((distance - target.HitboxRadius - BotGapClosers.Range + ApproachMargin) / AiSprintSpeed, () => GapClose(target));
            return true;
        }
        var destination = Position + direction * MathF.Min(distance - stop, BotGapClosers.Range);
        for (var step = 0; step < 10 && isOutsideArena(destination); step++)
            destination = Vector3.Lerp(destination, Position, 0.1f);
        var dash = destination - Position;
        PlayActionOn(actionId, target);
        Push(MathF.Atan2(dash.X, dash.Z), dash.Length(), DashSeconds);
        gapCloserReadyAt = events.Elapsed + (Natives.Data.Action(actionId)?.RecastSeconds ?? 0f);
        combatHold = MathF.Max(combatHold, DashHoldSeconds);
        return true;
    }

    private const float AiSprintSpeed = AnoMech.Core.Game.Ai.AiManager.SprintSpeed;

    // A bot's button press: the animation, then what a player's press does, statuses it spends
    // first. `target` is who a single-target action is aimed at (the boss, for Feint); else itself.
    public void UseAction(uint actionId, SimCharacter? target = null)
    {
        PlayAction(actionId);
        combatHold = MathF.Max(combatHold, PressHoldSeconds);
        ResolveEffects(actionId, target);
    }

    // What a player's press does beyond its animation: the statuses it spends and grants.
    internal void ResolveEffects(uint actionId, SimCharacter? target = null)
    {
        JobActions.ClearStatuses(this, actionId);
        JobActions.ApplyEffects(this, actionId, (ulong)(target ?? this).GameObjectId, actionRng().Source);
    }

    // level 1-3. The action id used; 0 if KO'd, actions are locked, or the job has no limit break at
    // that level. Bots press their limit break whenever a script calls for it, whatever the
    // mitigation settings: a real tank always LB3s Pandora's Box.
    internal uint UseLimitBreak(int level)
    {
        if (!this.IsAlive() || ActionsLocked) return 0;
        var actionId = LimitBreakHandler.ActionId(ClassJob, level);
        if (actionId == 0) return 0;
        UseAction(actionId);
        combatHold = LimitBreakHoldSeconds;
        DiagnosticLog.Info($"[SimPartyNpc] {Role} (job {ClassJob}) uses LB{level} {Natives.Data.ActionName(actionId)}.");
        return actionId;
    }

    public void UseSprint(float duration)
    {
        if (!ActionsLocked) SprintHandler.Apply(this, duration);
    }

    public override void Despawn()
    {
        base.Despawn();
    }

    // Weapon out for the whole run, as a player's is in combat, so attacks use the job's animations.
    protected override void OnPacketSpawned(IBattleCharaProxy chara)
    {
        ActorControl.ZoneIn();
        ActorControl.SetWeaponDrawn(true);
    }

    public void OnKilled(bool byEnvironment)
    {
        GoDown(byEnvironment);
    }
}
