using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Core.SimObjects;
using static AnoMech.Scenarios.Fru.FruConstants;
using static AnoMech.Scenarios.Fru.P4CrystallizeTime.FruP4CrystallizeTimeRoutes;
using static AnoMech.Scenarios.Fru.P4CrystallizeTime.FruP4CrystallizeTimeState;

namespace AnoMech.Scenarios.Fru.P4CrystallizeTime;

internal enum CrystallizeTimeAssignment { AeroWest, IceWest, Eruption, Ice, Darkness, Water, AeroEast, IceEast }

public sealed class FruP4CrystallizeTimeAi : IScenarioAi<FruP4CrystallizeTimeState>
{
    public string Name => "NA priority / 7-1 Akh Morn (MT solo)";
    public string Group => "NA";

    private const float RunSpeed = 6f;
    private const float ReferenceScale = 10f / 23.74f;
    private const float SpiritTakerAt = 48.5f;
    private const float SpiritTakerJitter = 0.3f;
    private const float AeroKnockbackEndsAt = 27.45f;
    private const float StopCleansingAt = 41.1f;
    private const float ArrivalMargin = 0.25f;

    private static readonly PartyRole[] Priority =
        [PartyRole.ShieldHealer, PartyRole.RegenHealer, PartyRole.OffTank, PartyRole.MainTank,
         PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps];

    private FruP4CrystallizeTimeState state = null!;
    private SimWorld world = null!;
    private readonly Dictionary<PartyRole, CrystallizeTimeAssignment> assignments = new();
    private readonly Dictionary<PartyRole, CrystallizeTimeAssignment> puddleTargets = new();
    private readonly HashSet<PartyRole> knockedByAero = [];
    private readonly HashSet<PartyRole> holdingClaw = [];
    private readonly HashSet<PartyRole> holdingFang = [];
    private readonly Dictionary<PartyRole, int> moveOrders = new();
    private float elapsed;

    public void Run(FruP4CrystallizeTimeState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        elapsed = 0f;
        puddleTargets.Clear();
        knockedByAero.Clear();
        holdingClaw.Clear();
        holdingFang.Clear();
        moveOrders.Clear();
        AssignSidesByPriority();

        world.Events.Add(15.5f, () => FollowRoute(state.SlowNorthwest ? PRE_HG_1_NW : PRE_HG_1_NE, arriveBy: 22.7f));
        world.Events.Add(23.3f, () => FollowRoute(state.SlowNorthwest ? POST_HG_1_NW : POST_HG_1_NE, arriveBy: 25.9f));
        world.Events.Add(25.9f, RememberWhoAeroKnocksBack);
        world.Events.Add(26f, () => FollowRoute(PUDDLE_DODGE));
        world.Events.Add(26.7f, () => FollowRoute(state.SlowNorthwest ? POST_KB_NW : POST_KB_NE, arriveBy: 28.2f));
        world.Events.Add(28.5f, () => FollowRoute(state.SlowNorthwest ? POST_HG_2_NW : POST_HG_2_NE, arriveBy: 28.8f));
        world.Events.Add(29.8f, () => FollowRoute(state.East ? POST_UD_E : POST_UD_W, arriveBy: 31.5f));
        world.Events.Add(31.5f, () => FollowRoute(state.East ? POST_EARLY_SOAK_E : POST_EARLY_SOAK_W, arriveBy: 32f));
        world.Events.Add(33.6f, () => FollowRoute(state.East ? POST_HG_3_E : POST_HG_3_W, arriveBy: 34.1f));
        world.Events.Add(34.6f, () => FollowRoute(state.East ? POST_EXA_2_E : POST_EXA_2_W, arriveBy: 36.1f));
        world.Events.Add(36.5f, () => FollowRoute(state.Corner switch
        {
            CrystallizeCorner.NW => POST_EXA_3_NW,
            CrystallizeCorner.NE => POST_EXA_3_NE,
            CrystallizeCorner.SE => POST_EXA_3_SE,
            _ => POST_EXA_3_SW,
        }, arriveBy: 38.1f));
        world.Events.Add(38.9f, () => FollowRoute(state.Corner switch
        {
            CrystallizeCorner.NW => POST_EXA_4_NW,
            CrystallizeCorner.NE => POST_EXA_4_NE,
            CrystallizeCorner.SE => POST_EXA_4_SE,
            _ => POST_EXA_4_SW,
        }, arriveBy: 40.5f));
        world.Events.Add(StopCleansingAt, () => MoveEveryBot(RewindSpot, arriveBy: 42.5f));
        world.Events.Add(ReturnSavedAt + 0.05f, () => MoveEveryBotForSpiritTaker());
        world.Events.Add(61.7f, () => MoveEveryBot(AkhMornSpot, arriveBy: 68.9f));
    }

    public void Tick(FruP4CrystallizeTimeState stateParam, SimWorld worldParam, float delta)
    {
        elapsed += delta;
        foreach (var bot in Bots())
        {
            var role = ((ISimPartyMember)bot).Role;
            ReactToClawCaught(bot, role);
            ReactToFangCleansed(bot, role);
        }
        ChasePuddles();
    }

    private void AssignSidesByPriority()
    {
        assignments.Clear();
        foreach (var role in Enum.GetValues<PartyRole>())
            assignments[role] = state.Debuff(role) switch
            {
                CrystallizeDebuff.RedAero => IsWestOfPair(role) ? CrystallizeTimeAssignment.AeroWest : CrystallizeTimeAssignment.AeroEast,
                CrystallizeDebuff.RedIce => IsWestOfPair(role) ? CrystallizeTimeAssignment.IceWest : CrystallizeTimeAssignment.IceEast,
                CrystallizeDebuff.BlueEruption => CrystallizeTimeAssignment.Eruption,
                CrystallizeDebuff.BlueIce => CrystallizeTimeAssignment.Ice,
                CrystallizeDebuff.BlueDarkness => CrystallizeTimeAssignment.Darkness,
                _ => CrystallizeTimeAssignment.Water,
            };
    }

    private bool IsWestOfPair(PartyRole role) => Priority.First(r => state.Debuff(r) == state.Debuff(role)) == role;

    private PartyRole RoleOf(CrystallizeTimeAssignment assignment) => assignments.First(a => a.Value == assignment).Key;

    private IEnumerable<SimCharacter> Bots() => world.Party.ActiveMembers().Where(world.Party.IsBotDriven).ToList();

    private void FollowRoute(IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination> route, float? arriveBy = null)
    {
        foreach (var (assignment, destination) in route)
        {
            var role = RoleOf(assignment);
            puddleTargets.Remove(role);
            if (world.Party.Get(role) is not { } bot || !bot.IsAlive() || !world.Party.IsBotDriven(bot)) continue;
            if (destination.Puddle is { } source)
            {
                CancelPendingMove(role);
                puddleTargets[role] = source;
            }
            else
            {
                var leaveNoSoonerThan = knockedByAero.Contains(role) ? AeroKnockbackEndsAt : 0f;
                MoveLeavingAsLateAsPossible(bot, role, destination.Position, arriveBy, leaveNoSoonerThan);
            }
        }
    }

    private void MoveEveryBot(Func<PartyRole, Vector3> spot, float arriveBy)
    {
        puddleTargets.Clear();
        foreach (var bot in Bots())
        {
            var role = ((ISimPartyMember)bot).Role;
            MoveLeavingAsLateAsPossible(bot, role, spot(role), arriveBy);
        }
    }

    private void MoveLeavingAsLateAsPossible(SimCharacter bot, PartyRole role, Vector3 destination, float? arriveBy, float leaveNoSoonerThan = 0f)
    {
        var walk = Vector2.Distance(new Vector2(bot.Position.X, bot.Position.Z), new Vector2(destination.X, destination.Z)) / RunSpeed;
        var leaveAt = MathF.Max(leaveNoSoonerThan, arriveBy is { } deadline ? deadline - ArrivalMargin - walk : 0f);
        var order = CancelPendingMove(role);
        if (leaveAt <= elapsed)
        {
            bot.MoveTo(destination, RunSpeed);
            return;
        }
        world.Events.Add(leaveAt - elapsed, () =>
        {
            if (moveOrders[role] == order && bot.IsAlive()) bot.MoveTo(destination, RunSpeed);
        });
    }

    private void MoveNow(SimCharacter bot, PartyRole role, Vector3 destination)
    {
        CancelPendingMove(role);
        bot.MoveTo(destination, RunSpeed);
    }

    private int CancelPendingMove(PartyRole role) => moveOrders[role] = moveOrders.GetValueOrDefault(role) + 1;

    private void RememberWhoAeroKnocksBack()
    {
        var aeros = state.Holders(CrystallizeDebuff.RedAero).Select(world.Party.Get).OfType<SimCharacter>().ToList();
        foreach (var member in world.Party.ActiveMembers())
            if (member is ISimPartyMember slot && aeros.Any(a => a != member && Vector3.Distance(a.Position, member.Position) <= 15f))
                knockedByAero.Add(slot.Role);
    }

    private void ReactToClawCaught(SimCharacter bot, PartyRole role)
    {
        var holds = bot.HasStatus(StatusId.Wyrmclaw);
        if (holds) { holdingClaw.Add(role); return; }
        if (!holdingClaw.Remove(role) || state.Debuff(role) != CrystallizeDebuff.RedAero) return;
        var escape = state.East ? POST_EARLY_SOAK_E : POST_EARLY_SOAK_W;
        MoveNow(bot, role, escape[assignments[role]].Position);
    }

    private void ReactToFangCleansed(SimCharacter bot, PartyRole role)
    {
        var holds = bot.HasStatus(StatusId.Wyrmfang);
        if (holds) { holdingFang.Add(role); return; }
        if (!holdingFang.Remove(role) || elapsed >= StopCleansingAt) return;
        puddleTargets.Remove(role);
        if (AfterCleanse.TryGetValue(assignments[role], out var next)) MoveNow(bot, role, next.Position);
    }

    private IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination> AfterCleanse => state.Corner switch
    {
        CrystallizeCorner.NW => POST_SOAK_TARGET_NW,
        CrystallizeCorner.NE => POST_SOAK_TARGET_NE,
        CrystallizeCorner.SE => POST_SOAK_TARGET_SE,
        _ => POST_SOAK_TARGET_SW,
    };

    private void ChasePuddles()
    {
        foreach (var (role, source) in puddleTargets)
            if (world.Party.Get(role) is { } bot && bot.IsAlive()
                && state.DragonPuddles.TryGetValue(RoleOf(source), out var puddle))
                bot.MoveTo(puddle, RunSpeed);
    }

    private Vector3 RewindSpot(PartyRole role)
    {
        var groupOne = role is PartyRole.MainTank or PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.PhysRangedDps;
        var tank = role.IsTank();
        var leadEast = state.Corner is CrystallizeCorner.NW or CrystallizeCorner.SE ? groupOne : !groupOne;
        var front = tank ? 22.7f : 20.5f;
        var back = tank ? 18.2f : 15.4f;
        var north = (state.North ? 1 : -1) * (leadEast ? back : front);
        var east = (state.East ? 1 : -1) * (leadEast ? front : back);
        return new Vector3(east * ReferenceScale, 0, -north * ReferenceScale);
    }

    private static Vector3 AkhMornSpot(PartyRole role) => role == PartyRole.MainTank ? new Vector3(-9.3f, 0, 0) : Vector3.Zero;

    // Spirit Taker's spread, as fractions of the arena radius: how far towards the rewind corner, and how far to the
    // side. Group one (main tank, regen healer, melee A, physical ranged) stands left of the corner's direction, group two right.
    private static (float Forward, float Side) SpiritTakerLayout(PartyRole role) => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => (0.82f, 0.22f),
        PartyRole.RegenHealer or PartyRole.ShieldHealer => (0.18f, 0.22f),
        PartyRole.MeleeDpsA or PartyRole.MeleeDpsB => (-0.19f, 0.34f),
        _ => (-0.04f, 0.87f),
    };

    private Vector3 SpiritTakerSpot(PartyRole role)
    {
        var groupOne = role is PartyRole.MainTank or PartyRole.RegenHealer or PartyRole.MeleeDpsA or PartyRole.PhysRangedDps;
        var forward = Vector3.Normalize(new Vector3(state.East ? 1f : -1f, 0, state.North ? -1f : 1f));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var (ahead, side) = SpiritTakerLayout(role);
        return (forward * ahead + right * side * (groupOne ? -1f : 1f)) * FruConstants.Geometry.ArenaRadius;
    }

    // Right after the rewinds are placed, each bot goes to its spot, sprinting when walking would be too late.
    private void MoveEveryBotForSpiritTaker()
    {
        puddleTargets.Clear();
        var rng = world.Stream("ct-spirit-taker");
        var fragmentRadius = world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.FragmentOfFate)?.HitboxRadius ?? 0f;
        var reach = (Natives.Data.Action(ActionId.SpiritTaker)?.EffectRange ?? 0f) + fragmentRadius + 0.05f;
        foreach (var bot in Bots())
        {
            var role = ((ISimPartyMember)bot).Role;
            var spot = SpiritTakerSpot(role);
            var angle = rng.NextSingle() * MathF.Tau;
            var destination = spot + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * (rng.NextSingle() * SpiritTakerJitter);
            // A jittered spot must not bring Spirit Taker's circle onto the Fragment of Fate; the spot itself is clear.
            if (Vector3.Distance(destination, FragmentPosition) <= reach) destination = spot;
            var delay = world.BotTiming == BotTiming.HumanLike ? world.AiReactionBase(role) : 0f;
            var distance = Vector2.Distance(new Vector2(bot.Position.X, bot.Position.Z), new Vector2(destination.X, destination.Z));
            var sprint = delay + distance / RunSpeed > SpiritTakerAt - ArrivalMargin - elapsed && bot is SimPartyNpc;
            var order = CancelPendingMove(role);
            world.Events.Add(Math.Max(delay, 0.01f), () =>
            {
                if (moveOrders[role] != order || !bot.IsAlive()) return;
                if (sprint) ((SimPartyNpc)bot).UseSprint(distance / AiManager.SprintSpeed + 1f);
                bot.MoveTo(destination, sprint ? AiManager.SprintSpeed : RunSpeed);
            });
        }
    }
}
