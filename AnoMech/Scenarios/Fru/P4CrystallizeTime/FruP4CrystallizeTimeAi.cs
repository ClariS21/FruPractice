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

    private const float Walk = 6.5f;
    private const float ReferenceScale = 10f / 23.74f;
    private const float AfterHit = 0.05f;
    private const float HourglassesTetheredAt = 15.15f;
    // The party holds together in the middle, as long as it still makes its spot in time, until the healers' mitigation
    // for the mechanic is out at 18.92 s: it only reaches those near the caster.
    private const float PartyMitigatedAt = 18.95f;
    private static readonly float[] HourglassBurstAt = [22.7f, 28.2f, 33.15f];
    private static readonly float[][] TidalLightAt = [[32f, 34.1f, 36.1f, 38.1f], [38.4f, 40.5f, 42.5f, 44.5f]];
    private const float DarkWaterStackAt = 23.9f;
    private const float FireworksAt = 25.9f;
    private const float IceDodgeDoneAt = 26.45f;
    private const float AeroKnockbackEndsAt = FireworksAt + 0.81f + 30f / 60f + AfterHit;
    private const float UnholyDarknessAt = 28.8f;
    private const float HeadsReachAeroSpotsAt = 30.25f;
    private const float EarlySoakFallbackAt = 31.5f;
    private const float SpiritTakerSpreadAt = ReturnSavedAt + AfterHit;
    private const float SpiritTakerAt = 48.5f;
    private const float AkhMornStepAt = StunEndsAt + 0.2f;
    private const float AkhMornAt = 69f;
    private const float TightRoom = 0.4f;
    // The Aero knockback throws the blues 30 y along the line from the Aero: an offset at the setup swings where they land.
    private const float KnockbackSetupRoom = 0.1f;
    private const float CatchRoom = 0.3f;
    // Some puddles lie a few tenths of a yalm off a Tidal Light strip.
    private const float PuddleRoom = 0f;
    private const float StackRoom = 1f;
    private const float RewindRoom = 0.3f;
    private const float SpiritTakerRoom = 0.3f;
    private const float SoloAkhMornRoom = 1f;
    private const float AkhMornStackRoom = 1.5f;
    private const float PuddleRush = 0.3f;
    private const float PuddleReach = 1f;
    private const float PuddleKeepOut = PuddleReach + 0.5f;
    private const float DetourRoom = 0.2f;
    private const float DetourSlack = 0.35f;
    private const float DetourPassing = 2f;
    private const float PastDetourKeepOut = PuddleReach + HazardMargin + 0.05f;
    private const int DetourBearings = 24;
    private static readonly float[] DetourOffsets = [0.4f, 0.8f, 1.4f, 2.2f, 3.2f];
    private const float PlainArrivalMargin = 0.25f;
    private const float HazardMargin = 0.1f;
    private const float FrameReserve = 0.1f;
    private const float PathSample = 0.1f;
    // The Aero knockback lands the blues short of the Unholy Darkness stack: they walk at least this far into it.
    private const float KnockedIntoStack = 2.5f;

    private static readonly float RewindAt = TidalLightAt[1][1] + AfterHit;

    private static readonly PartyRole[] Priority =
        [PartyRole.ShieldHealer, PartyRole.RegenHealer, PartyRole.OffTank, PartyRole.MainTank,
         PartyRole.MeleeDpsA, PartyRole.MeleeDpsB, PartyRole.PhysRangedDps, PartyRole.CasterDps];

    private FruP4CrystallizeTimeState state = null!;
    private SimWorld world = null!;
    private float startedAt;
    private readonly Dictionary<PartyRole, CrystallizeTimeAssignment> assignments = new();
    private readonly Dictionary<PartyRole, CrystallizeTimeAssignment> awaitingPuddle = new();
    private readonly HashSet<PartyRole> holdingClaw = [];
    private readonly HashSet<PartyRole> holdingFang = [];
    private readonly HashSet<PartyRole> sentToEarlySoak = [];
    private readonly HashSet<PartyRole> cleansing = [];
    private readonly Dictionary<PartyRole, PuddleDetour> detours = new();
    private readonly Dictionary<PartyRole, (Order Order, int Number)> walks = new();
    private readonly Dictionary<PartyRole, Vector3> puddlesSeen = new();
    private readonly int[] orders = new int[8];
    private List<Step> steps = [];
    private List<Hazard> hazards = [];

    private sealed record Step(float At, CueKind KnownBy, IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination> Route,
        Func<CrystallizeTimeAssignment, bool> Who, float? InBy = null, float? OutBy = null, Func<Vector3, Vector3, float>? Exit = null,
        float Room = TightRoom, float? GatherUntil = null);

    private sealed record Hazard(float At, Func<Vector3, float> Depth);

    private sealed record Clearance(float? InBy, float? OutBy, float? Exit, float? HoldUntil, bool WalkingIn);

    private sealed record Order(PartyRole Role, Vector3 Spot, CueKind KnownBy, float Room, float? InBy, float? OutBy, Func<Vector3, Vector3, float>? Exit,
        bool Cleanses, float? GatherUntil, float CueAt, bool DetourLeg = false, float KeepOut = PuddleKeepOut);

    private sealed record PuddleDetour(Order Order, int Number, Vector3 Via, IReadOnlyList<PartyRole> Puddles, bool UntilGone);

    private float Now => world.Events.Elapsed - startedAt;

    public void Run(FruP4CrystallizeTimeState stateParam, SimWorld worldParam)
    {
        state = stateParam;
        world = worldParam;
        startedAt = world.Events.Elapsed;
        awaitingPuddle.Clear();
        holdingClaw.Clear();
        holdingFang.Clear();
        sentToEarlySoak.Clear();
        cleansing.Clear();
        detours.Clear();
        walks.Clear();
        puddlesSeen.Clear();
        Array.Clear(orders);
        AssignSidesByPriority();
        hazards = Hazards().OrderBy(h => h.At).ToList();
        steps = Steps().ToList();
        foreach (var step in steps) world.Events.Add(step.At, () => Take(step));
        world.Events.Add(RewindAt, MoveEveryBotToItsRewind);
        world.Events.Add(SpiritTakerSpreadAt, MoveEveryBotForSpiritTaker);
        world.Events.Add(AkhMornStepAt, MoveEveryBotForAkhMorn);
    }

    // The human's seat reacts too: its MoveTo is inert in game, but the spot goes on its trail for the guide and the death recap.
    public void Tick(FruP4CrystallizeTimeState stateParam, SimWorld worldParam, float delta)
    {
        foreach (var member in world.Party.ActiveMembers().ToList())
        {
            var role = ((ISimPartyMember)member).Role;
            ReactToClawCaught(member, role);
            ReactToFangCleansed(member, role);
        }
        ReplanWalksAcrossNewPuddles();
        HeadForPuddlesAsTheyDrop();
        ResumeDetoursPastPuddles();
    }

    private IEnumerable<Step> Steps()
    {
        var northwest = state.SlowNorthwest;
        var loneAero = northwest ? CrystallizeTimeAssignment.AeroWest : CrystallizeTimeAssignment.AeroEast;
        var preHourglass1 = northwest ? PRE_HG_1_NW : PRE_HG_1_NE;
        var postHourglass1 = northwest ? POST_HG_1_NW : POST_HG_1_NE;
        var postKnockback = northwest ? POST_KB_NW : POST_KB_NE;
        var postHourglass2 = northwest ? POST_HG_2_NW : POST_HG_2_NE;
        yield return new(DebuffsAt + AfterHit, CueKind.Debuff, preHourglass1, IsRed, GatherUntil: PartyMitigatedAt);
        yield return new(HourglassesTetheredAt, CueKind.Cast, preHourglass1, a => !IsRed(a), GatherUntil: PartyMitigatedAt);
        yield return new(HourglassBurstAt[0] + AfterHit, CueKind.Resolve, postHourglass1, a => a == loneAero, InBy: FireworksAt);
        yield return new(DarkWaterStackAt + AfterHit, CueKind.Resolve, postHourglass1, a => a != loneAero, InBy: FireworksAt, Room: KnockbackSetupRoom);
        yield return new(FireworksAt + AfterHit, CueKind.Resolve, PUDDLE_DODGE, IsRedIce);
        yield return new(FireworksAt + AfterHit, CueKind.Resolve, postKnockback, IsAero);
        yield return new(FireworksAt + AfterHit, CueKind.Resolve, postKnockback, a => a == CrystallizeTimeAssignment.Eruption, InBy: UnholyDarknessAt,
            Room: StackRoom);
        yield return new(IceDodgeDoneAt, CueKind.Resolve, postKnockback, IsRedIce);
        yield return new(AeroKnockbackEndsAt, CueKind.Resolve, postKnockback, IsKnockedAcrossWithTheDarkness, OutBy: UnholyDarknessAt,
            Exit: IntoTheStack, Room: StackRoom);
        yield return new(AeroKnockbackEndsAt, CueKind.Resolve, postKnockback, a => a == CrystallizeTimeAssignment.Darkness, OutBy: UnholyDarknessAt,
            Exit: StackOffTheFragment, Room: StackRoom);
        yield return new(HourglassBurstAt[1] + AfterHit, CueKind.Resolve, postHourglass2, IsAero, InBy: HeadsReachAeroSpotsAt, Room: CatchRoom);
        yield return new(HourglassBurstAt[1] + AfterHit, CueKind.Resolve, postHourglass2, IsRedIce);
        yield return new(UnholyDarknessAt + AfterHit, CueKind.Resolve, state.East ? POST_UD_E : POST_UD_W, _ => true);
        yield return new(EarlySoakFallbackAt, CueKind.Cast, EarlySoak, a => !sentToEarlySoak.Contains(RoleOf(a)));
        yield return new(HourglassBurstAt[2] + AfterHit, CueKind.Resolve, state.East ? POST_HG_3_E : POST_HG_3_W, _ => true);
        yield return new(TidalLightAt[0][1] + AfterHit, CueKind.Resolve, state.East ? POST_EXA_2_E : POST_EXA_2_W, _ => true);
        yield return new(TidalLightAt[0][2] + AfterHit, CueKind.Resolve, state.Corner switch
        {
            CrystallizeCorner.NW => POST_EXA_3_NW,
            CrystallizeCorner.NE => POST_EXA_3_NE,
            CrystallizeCorner.SE => POST_EXA_3_SE,
            _ => POST_EXA_3_SW,
        }, _ => true);
        yield return new(TidalLightAt[1][0] + AfterHit, CueKind.Resolve, state.Corner switch
        {
            CrystallizeCorner.NW => POST_EXA_4_NW,
            CrystallizeCorner.NE => POST_EXA_4_NE,
            CrystallizeCorner.SE => POST_EXA_4_SE,
            _ => POST_EXA_4_SW,
        }, _ => true);
    }

    private IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination> EarlySoak => state.East ? POST_EARLY_SOAK_E : POST_EARLY_SOAK_W;

    private IReadOnlyDictionary<CrystallizeTimeAssignment, CrystallizeTimeDestination> AfterCleanse => state.Corner switch
    {
        CrystallizeCorner.NW => POST_SOAK_TARGET_NW,
        CrystallizeCorner.NE => POST_SOAK_TARGET_NE,
        CrystallizeCorner.SE => POST_SOAK_TARGET_SE,
        _ => POST_SOAK_TARGET_SW,
    };

    private static bool IsAero(CrystallizeTimeAssignment a) => a is CrystallizeTimeAssignment.AeroWest or CrystallizeTimeAssignment.AeroEast;
    private static bool IsRedIce(CrystallizeTimeAssignment a) => a is CrystallizeTimeAssignment.IceWest or CrystallizeTimeAssignment.IceEast;
    private static bool IsRed(CrystallizeTimeAssignment a) => IsAero(a) || IsRedIce(a);

    private static bool IsKnockedAcrossWithTheDarkness(CrystallizeTimeAssignment a) => a is CrystallizeTimeAssignment.Ice or CrystallizeTimeAssignment.Water;

    private float StackOffTheFragment(Vector3 from, Vector3 to) => MathF.Max(KnockedIntoStack, ClearAfter(from, to, UnholyDarknessOnTheFragment, 0f));

    // How far short of the stack the knockback lands the Ice and the Water depends on where the Aero stood when it went off: they walk
    // until they are in it, around the Darkness's spot.
    private float IntoTheStack(Vector3 from, Vector3 to)
    {
        var stack = (state.SlowNorthwest ? POST_KB_NW : POST_KB_NE)[CrystallizeTimeAssignment.Darkness].Position;
        var reach = (Natives.Data.Action(ActionId.UnholyDarkness)?.EffectRange ?? 6f) - StackRoom - HazardMargin;
        return MathF.Max(KnockedIntoStack, ClearAfter(from, to, p => Flat(p, stack) - reach, 0f));
    }

    private float UnholyDarknessOnTheFragment(Vector3 holder)
        => (Natives.Data.Action(ActionId.UnholyDarkness)?.EffectRange ?? 6f) + FragmentRadius + HazardMargin - Flat(holder, FragmentPosition);

    private float FragmentRadius => world.Children.OfType<SimEnemy>().FirstOrDefault(e => e.BNpcBaseId == BNpcBaseId.FragmentOfFate)?.HitboxRadius ?? 0f;

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

    private void Take(Step step)
    {
        foreach (var (assignment, destination) in step.Route.OrderBy(entry => entry.Value.Puddle is null))
        {
            if (!step.Who(assignment)) continue;
            var role = RoleOf(assignment);
            awaitingPuddle.Remove(role);
            if (destination.Puddle is not { } source)
                Send(role, destination.Position, step.KnownBy, step.Room, step.InBy, step.OutBy, step.Exit, gatherUntil: step.GatherUntil);
            else if (state.DragonPuddles.TryGetValue(RoleOf(source), out var puddle))
                SendToPuddle(role, puddle, step.KnownBy);
            else
            {
                orders[(int)role]++;
                awaitingPuddle[role] = source;
                cleansing.Add(role);
            }
        }
    }

    private void HeadForPuddlesAsTheyDrop()
    {
        foreach (var (role, source) in awaitingPuddle.ToList())
            if (state.DragonPuddles.TryGetValue(RoleOf(source), out var puddle))
            {
                awaitingPuddle.Remove(role);
                SendToPuddle(role, puddle, CueKind.Resolve);
            }
    }

    private void ReactToClawCaught(SimCharacter member, PartyRole role)
    {
        if (member.HasStatus(StatusId.Wyrmclaw)) { holdingClaw.Add(role); return; }
        if (!holdingClaw.Remove(role) || state.Debuff(role) != CrystallizeDebuff.RedAero) return;
        sentToEarlySoak.Add(role);
        Send(role, EarlySoak[assignments[role]].Position, CueKind.Resolve, TightRoom);
    }

    private void ReactToFangCleansed(SimCharacter member, PartyRole role)
    {
        if (member.HasStatus(StatusId.Wyrmfang)) { holdingFang.Add(role); return; }
        if (!holdingFang.Remove(role)) return;
        awaitingPuddle.Remove(role);
        if (!cleansing.Remove(role) || Now >= RewindAt) return;
        if (AfterCleanse.TryGetValue(assignments[role], out var next)) Send(role, next.Position, CueKind.Debuff, TightRoom);
    }

    private void SendToPuddle(PartyRole role, Vector3 puddle, CueKind knownBy)
    {
        cleansing.Add(role);
        Send(role, puddle, knownBy, PuddleRoom, cleanses: true);
    }

    internal void Send(PartyRole role, Vector3 spot, CueKind knownBy, float room, float? inBy = null, float? outBy = null,
        Func<Vector3, Vector3, float>? exit = null, bool cleanses = false, float? gatherUntil = null)
    {
        if (!cleanses) cleansing.Remove(role);
        Issue(new Order(role, spot, knownBy, room, inBy, outBy, exit, cleanses, gatherUntil, Now), ++orders[(int)role], given: true);
    }

    private void Issue(Order o, int order, bool given = false)
    {
        var role = o.Role;
        if (orders[(int)role] != order || world.Party.Get(role) is not { } member || !member.IsAlive()) return;
        if (!o.DetourLeg) walks[role] = (o, order);
        var now = Now;
        var assignment = assignments[role];
        var spot = o.Spot;
        var travel = Flat(member.Position, spot);
        // While an order waits, the seat keeps walking its last one: it may set off from that walk's end, as HabitMovement plans.
        var drift = member.MoveDestination is { } heading ? MathF.Max(0f, Flat(heading, spot) - travel) : 0f;
        var puddles = PuddlesToKeepOutOf(member, o);
        var room = MathF.Max(0f, MathF.Min(MathF.Min(o.Room, RoomClearOfPuddles(StartsOf(member), spot, puddles)),
            FruConstants.Geometry.ArenaRadius - HazardMargin - MathF.Max(Flat(member.Position, Vector3.Zero), Flat(spot, Vector3.Zero))));
        // The human's seat: the human walks it in game, where its MoveTo is inert, and a stand-in in tests.
        var humanSeat = member is not SimPartyNpc;
        var habitTimed = !humanSeat && world.BotTiming == BotTiming.HumanLike;
        var arrivalMargin = habitTimed ? AiManager.StaggeredArrivalMargin + FrameReserve : PlainArrivalMargin;
        var nextStep = NextStepAt(assignment, now);
        Vector3? cleanseThenTo = o.Cleanses && AfterCleanse.TryGetValue(assignment, out var next) ? next.Position : null;
        var clearance = ClearanceOf(member.Position, member.MoveDestination, spot, now, nextStep, room, arrivalMargin, cleanseThenTo, NextSpot(role, now));
        var inBy = Earliest(Earliest(o.InBy, clearance.InBy), o.Cleanses ? nextStep + PuddleReach / Walk : null);
        var (outBy, exit) = clearance.OutBy is { } hazardOutBy && (o.OutBy is null || hazardOutBy < o.OutBy)
            ? (hazardOutBy, clearance.Exit)
            : (o.OutBy, o.Exit?.Invoke(member.Position, spot));
        if (o.Cleanses || clearance.WalkingIn) inBy = Earliest(inBy, now + travel / Walk + arrivalMargin + (habitTimed ? PuddleRush : 0f));
        if (exit >= travel - room) exit = null;
        // Without the habit layer there is no outBy: being out of the danger in time is an arrival of its own.
        if ((inBy is not null || !habitTimed) && outBy is { } dangerAt) inBy = Earliest(inBy, dangerAt + (travel - (exit ?? travel)) / Walk);
        // The human's seat has its spot on the trail as the step gives it, for the path guidance and the death recap: every wait
        // below is the stand-in's alone.
        if (given && humanSeat) world.Strat.Note(role, new Vector2(spot.X, spot.Z), deadline: startedAt + inBy);
        if (TakeTheWayAroundPuddles(member, o, order, puddles, inBy)) return;
        if (clearance.HoldUntil is { } hold && hold + AfterHit - now > PathSample / Walk)
        {
            world.Events.Add(hold + AfterHit - now, () => Issue(o, order));
            return;
        }
        var leaveBy = Earliest(inBy - arrivalMargin - (travel + drift) / Walk, outBy - arrivalMargin - ((exit ?? travel) + drift) / Walk);
        if (o.GatherUntil is { } gather && MathF.Min(gather, leaveBy ?? gather) - now is > PathSample / Walk and var gathering)
        {
            world.Events.Add(gathering, () => Issue(o, order));
            return;
        }
        if (!habitTimed)
        {
            if (inBy - PlainArrivalMargin - FrameReserve - (travel + drift) / Walk - now is > PathSample / Walk and var latest)
            {
                world.Events.Add(latest, () => Issue(o, order));
                return;
            }
            (outBy, exit) = (null, null);
        }
        if (humanSeat)
        {
            WalkTheHumanSeat(member, role, spot, inBy, now, drift, order);
            return;
        }
        // A plain bot sets off now: its wait is over, and a delayed legacy move would keep it on its last walk meanwhile.
        new AiManager(world).Move(0f, () => AiMove.Single(role, new Vector2(spot.X, spot.Z)), jitter: 0f, knownAt: o.CueAt - now, knownBy: o.KnownBy,
            room: room, arrivalTime: inBy - now, outBy: outBy - now, exitDistance: exit,
            departureDelay: habitTimed ? null : _ => 0f);
    }

    // AiManager.Move would put the spot on the trail a second time, at the departure. The stand-in sets off as Move's plain path
    // would, after what slack is left, unless a newer order replaced this one in the meantime.
    private void WalkTheHumanSeat(SimCharacter member, PartyRole role, Vector3 spot, float? inBy, float now, float drift, int order)
    {
        var travel = Flat(member.Position, spot);
        var spare = inBy is { } by ? by - now - PlainArrivalMargin - travel / Walk : 0f;
        var sprint = spare < 0f;
        var slack = spare - drift / Walk;
        if (sprint) (member as ISimPartyMember)?.UseSprint(travel / AiManager.SprintSpeed + 1f);
        AnoMech.Core.DiagnosticLog.Info($"[CT] {role} ({member.Position.X:F1},{member.Position.Z:F1}) -> ({spot.X:F2},{spot.Z:F2}) {travel:F1}y"
            + (sprint ? " sprinting" : slack > 0f ? $", leaving in {slack:F2}s" : "") + ".");
        if (slack <= 0f) member.MoveTo(spot, sprint ? AiManager.SprintSpeed : Walk);
        else world.Events.Add(slack, () =>
        {
            if (orders[(int)role] == order && member.IsAlive()) member.MoveTo(spot, Walk);
        });
    }

    private static float RoomClearOfPuddles(IReadOnlyList<Vector3> starts, Vector3 to, IReadOnlyList<(PartyRole Catcher, Vector3 At)> puddles)
    {
        var room = float.PositiveInfinity;
        foreach (var (_, puddle) in puddles)
            foreach (var from in starts)
            {
                var miss = DistanceToPath(puddle, from, to) - PuddleReach - HazardMargin;
                if (miss > 0f) room = MathF.Min(room, miss / 2f);
            }
        return room;
    }

    private List<(PartyRole Catcher, Vector3 At)> PuddlesToKeepOutOf(SimCharacter member, Order o)
        => state.DragonPuddles
            .Where(p => p.Key != o.Role && !(o.Cleanses && member.HasStatus(StatusId.Wyrmfang) && Flat(p.Value, o.Spot) < 0.01f))
            .Select(p => (p.Key, p.Value))
            .ToList();

    private static IReadOnlyList<Vector3> StartsOf(SimCharacter member)
        => member.MoveDestination is { } heading ? [member.Position, heading] : [member.Position];

    private bool TakeTheWayAroundPuddles(SimCharacter member, Order o, int order, IReadOnlyList<(PartyRole Catcher, Vector3 At)> puddles, float? inBy)
    {
        if (o.DetourLeg || puddles.Count == 0) return false;
        var starts = StartsOf(member);
        var all = puddles.Select(p => p.At).ToList();
        var under = puddles.Where(p => Flat(o.Spot, p.At) < PuddleKeepOut).ToList();
        var target = under.Count == 0 ? o.Spot : BesidePuddles(o.Spot, member.Position, all);
        var crossed = puddles.Where(p => !SweepClear(starts, target, p.At, o.KeepOut)).ToList();
        if (under.Count == 0 && crossed.Count == 0) return false;
        var blocking = under.Concat(crossed).Select(p => p.Catcher).Distinct().ToList();
        var around = crossed.Select(p => p.At).ToList();
        if (crossed.Count > 0 && (WayAround(starts, target, all, around, ClearOfComingHazards) ?? WayAround(starts, target, all, around, _ => true)) is { } via)
        {
            detours[o.Role] = new(o, order, via, blocking, UntilGone: false);
            Issue(o with { Spot = via, Room = DetourRoom, InBy = inBy - Flat(via, target) / Walk, Exit = null, Cleanses = false, DetourLeg = true }, order);
            return true;
        }
        var wait = crossed.Count > 0 ? ShortOfPuddles(member.Position, target, all) : target;
        detours[o.Role] = new(o, order, wait, blocking, UntilGone: true);
        Issue(o with { Spot = wait, Room = DetourRoom, Exit = null, Cleanses = false, DetourLeg = true }, order);
        return true;
    }

    private void ResumeDetoursPastPuddles()
    {
        foreach (var (role, detour) in detours.ToList())
        {
            if (orders[(int)role] != detour.Number || world.Party.Get(role) is not { } member || !member.IsAlive())
            {
                detours.Remove(role);
                continue;
            }
            if (detour.UntilGone ? detour.Puddles.All(state.DragonPuddles.ContainsKey) : Flat(member.Position, detour.Via) > DetourSlack) continue;
            detours.Remove(role);
            Issue(detour.UntilGone ? detour.Order : detour.Order with { KeepOut = PastDetourKeepOut }, ++orders[(int)role]);
        }
    }

    private void ReplanWalksAcrossNewPuddles()
    {
        var dropped = state.DragonPuddles.Where(p => !puddlesSeen.TryGetValue(p.Key, out var seen) || Flat(seen, p.Value) > 0.01f).ToList();
        foreach (var gone in puddlesSeen.Keys.Where(catcher => !state.DragonPuddles.ContainsKey(catcher)).ToList()) puddlesSeen.Remove(gone);
        foreach (var (catcher, at) in dropped) puddlesSeen[catcher] = at;
        if (dropped.Count == 0) return;
        foreach (var (role, walk) in walks.ToList())
        {
            if (orders[(int)role] != walk.Number || world.Party.Get(role) is not { } member || !member.IsAlive())
            {
                walks.Remove(role);
                continue;
            }
            var keptOutOf = PuddlesToKeepOutOf(member, walk.Order).Select(p => p.Catcher).ToHashSet();
            if (!dropped.Any(p => keptOutOf.Contains(p.Key) && WalkMeetsPuddle(member, walk.Order, p.Value))) continue;
            detours.Remove(role);
            world.Movement.Supersede(role);
            if (world.Party.IsBotDriven(member)) member.StopMoving();
            Issue(walk.Order, ++orders[(int)role]);
        }
    }

    private bool WalkMeetsPuddle(SimCharacter member, Order o, Vector3 puddle)
    {
        var starts = StartsOf(member);
        if (starts.All(s => Flat(s, o.Spot) <= o.Room + DetourSlack)) return false;
        if (Flat(o.Spot, puddle) < PuddleKeepOut) return true;
        if (detours.TryGetValue(o.Role, out var detour) && detour.Number == orders[(int)o.Role])
            return !SweepClear(starts, detour.Via, puddle, o.KeepOut) || (!detour.UntilGone && !PathClear(detour.Via, o.Spot, puddle, o.KeepOut));
        return !SweepClear(starts, o.Spot, puddle, o.KeepOut);
    }

    private static Vector3 BesidePuddles(Vector3 spot, Vector3 from, IReadOnlyList<Vector3> puddles)
    {
        var beside = spot;
        for (var pass = 0; pass < 3; pass++)
            foreach (var puddle in puddles)
            {
                var away = new Vector2(beside.X - puddle.X, beside.Z - puddle.Z);
                if (away.Length() >= PuddleKeepOut + DetourRoom) continue;
                if (away.LengthSquared() < 1e-4f) away = new Vector2(from.X - puddle.X, from.Z - puddle.Z);
                if (away.LengthSquared() < 1e-4f) away = -Vector2.Normalize(new Vector2(puddle.X, puddle.Z) + Vector2.UnitY * 1e-3f);
                away = Vector2.Normalize(away) * (PuddleKeepOut + DetourRoom + 0.05f);
                beside = new Vector3(puddle.X + away.X, spot.Y, puddle.Z + away.Y);
            }
        return beside;
    }

    private static Vector3? WayAround(IReadOnlyList<Vector3> starts, Vector3 to, IReadOnlyList<Vector3> puddles, IEnumerable<Vector3> around,
        Func<(Vector3 Via, float Length), bool> safe)
    {
        Vector3? best = null;
        var shortest = float.PositiveInfinity;
        foreach (var centre in around)
            foreach (var offset in DetourOffsets)
                for (var bearing = 0; bearing < DetourBearings; bearing++)
                {
                    var angle = bearing * MathF.Tau / DetourBearings;
                    var via = centre + new Vector3(MathF.Cos(angle), 0f, MathF.Sin(angle)) * (PuddleKeepOut + offset);
                    if (Flat(via, Vector3.Zero) > FruConstants.Geometry.ArenaRadius - HazardMargin - DetourRoom) continue;
                    if (!puddles.All(p => SweepClear(starts, via, p, PuddleKeepOut + DetourSlack) && PathClear(via, to, p, PuddleKeepOut + DetourSlack)))
                        continue;
                    var length = Flat(starts[0], via) + Flat(via, to);
                    if (length < shortest && safe((via, length))) (best, shortest) = (via, length);
                }
        return best;
    }

    private bool ClearOfComingHazards((Vector3 Via, float Length) detour)
    {
        var now = Now;
        var passedBy = now + DetourPassing + detour.Length / Walk;
        return hazards.All(h => h.At <= now || h.At > passedBy || h.Depth(detour.Via) <= -(DetourRoom + HazardMargin));
    }

    private static Vector3 ShortOfPuddles(Vector3 from, Vector3 to, IReadOnlyList<Vector3> puddles)
    {
        var length = Flat(from, to);
        var reached = 0f;
        for (var s = 0f; s <= length; s += PathSample)
        {
            var point = PointAt(from, to, s, length);
            if (!puddles.All(p => Flat(point, p) >= MathF.Min(PuddleKeepOut, Flat(from, p)) - 0.01f)) break;
            reached = s;
        }
        return PointAt(from, to, reached, length);
    }

    private static bool SweepClear(IReadOnlyList<Vector3> starts, Vector3 to, Vector3 puddle, float clearance)
        => starts.All(s => PathClear(s, to, puddle, clearance)) && (starts.Count < 2 || !Within(puddle, starts[0], starts[1], to));

    private static bool PathClear(Vector3 from, Vector3 to, Vector3 puddle, float clearance)
        => DistanceToPath(puddle, from, to) >= MathF.Min(clearance, MathF.Min(Flat(from, puddle), Flat(to, puddle))) - 0.01f;

    private static bool Within(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        static float Side(Vector3 p, Vector3 a, Vector3 b) => (b.X - a.X) * (p.Z - a.Z) - (b.Z - a.Z) * (p.X - a.X);
        var (ab, bc, ca) = (Side(p, a, b), Side(p, b, c), Side(p, c, a));
        return (ab > 0f && bc > 0f && ca > 0f) || (ab < 0f && bc < 0f && ca < 0f);
    }

    private static float DistanceToPath(Vector3 point, Vector3 from, Vector3 to)
    {
        var length = Flat(from, to);
        if (length <= 0f) return Flat(point, from);
        var along = Math.Clamp(((point.X - from.X) * (to.X - from.X) + (point.Z - from.Z) * (to.Z - from.Z)) / (length * length), 0f, 1f);
        return Flat(point, PointAt(from, to, along * length, length));
    }

    private float NextStepAt(CrystallizeTimeAssignment assignment, float after)
        => new[] { NextRouteStep(assignment, after)?.At ?? float.PositiveInfinity, RewindAt, SpiritTakerSpreadAt, AkhMornStepAt }
            .Where(at => at > after + 0.01f).DefaultIfEmpty(float.PositiveInfinity).Min();

    private Step? NextRouteStep(CrystallizeTimeAssignment assignment, float after)
        => steps.Where(s => s.At > after + 0.01f && s.Route.ContainsKey(assignment) && s.Who(assignment)).MinBy(s => s.At);

    private Vector3? NextSpot(PartyRole role, float after)
    {
        var at = NextStepAt(assignments[role], after);
        if (NextRouteStep(assignments[role], after) is { } step && step.At <= at)
            return step.Route[assignments[role]] is { Puddle: { } source }
                ? state.DragonPuddles.TryGetValue(RoleOf(source), out var puddle) ? puddle : null
                : step.Route[assignments[role]].Position;
        return at == RewindAt ? RewindSpot(role) : at == SpiritTakerSpreadAt ? SpiritTakerSpot(role) : at == AkhMornStepAt ? AkhMornSpot(role) : null;
    }

    private Clearance ClearanceOf(Vector3 from, Vector3? heading, Vector3 to, float now, float until, float room, float arrivalMargin,
        Vector3? cleanseThenTo, Vector3? afterwards)
    {
        float? inBy = null, outBy = null, exit = null, holdUntil = null;
        var walkingIn = false;
        var margin = room + HazardMargin;
        var travel = Flat(from, to);
        var lookedAhead = false;
        foreach (var hazard in hazards)
        {
            if (hazard.At <= now) continue;
            var start = hazard.Depth(from);
            var end = hazard.Depth(to);
            if (hazard.At > until)
            {
                if (lookedAhead || end <= 0f) continue;
                lookedAhead = true;
                if (afterwards is { } then && hazard.Depth(then) <= -margin)
                    inBy = Earliest(inBy, hazard.At - ClearAfter(to, then, hazard.Depth, margin) / Walk);
            }
            else if (start > -HazardMargin || heading is { } toward && hazard.Depth(toward) > 0f)
            {
                if (end > 0f || outBy is not null) continue;
                walkingIn = start <= -HazardMargin;
                outBy = hazard.At;
                exit = ClearAfter(from, to, hazard.Depth, MathF.Min(margin, -end));
            }
            else if (cleanseThenTo is { } next && end > 0f && hazard.Depth(next) <= -(TightRoom + HazardMargin))
                inBy = Earliest(inBy, hazard.At - ClearAfter(to, next, hazard.Depth, TightRoom + HazardMargin) / Walk);
            else if (FirstNearer(from, to, hazard.Depth, MathF.Max(-margin, start + 0.01f)) is not null)
            {
                var leave = ClearAfter(from, to, hazard.Depth, margin);
                if (end <= -margin && now + leave / Walk <= hazard.At - arrivalMargin)
                    inBy = Earliest(inBy, hazard.At + (travel - leave) / Walk);
                else
                    holdUntil = MathF.Max(holdUntil ?? float.NegativeInfinity, hazard.At);
            }
        }
        return new(inBy, outBy, exit, holdUntil, walkingIn);
    }

    private IEnumerable<Hazard> Hazards()
    {
        var burst = (float?)Natives.Data.Action(ActionId.Maelstrom)?.EffectRange ?? 12f;
        for (var wave = 0; wave < HourglassBurstAt.Length; wave++)
        {
            var hourglasses = state.HourglassPair(wave).Select(Hourglass).ToArray();
            yield return new(HourglassBurstAt[wave], p => hourglasses.Max(h => burst - Flat(p, h)));
        }
        var depth = (float?)Natives.Data.Action(ActionId.TidalLightFirst)?.EffectRange ?? 10f;
        for (var set = 0; set < TidalLightAt.Length; set++)
        {
            var direction = state.WaveDirection(set == 1);
            for (var step = 0; step < TidalLightAt[set].Length; step++)
            {
                var origin = state.WaveOrigin(set == 1, step);
                yield return new(TidalLightAt[set][step], p =>
                {
                    var along = (p.X - origin.X) * direction.X + (p.Z - origin.Z) * direction.Z;
                    return MathF.Min(along, depth - along);
                });
            }
        }
    }

    private static float ClearAfter(Vector3 from, Vector3 to, Func<Vector3, float> depth, float clearOf)
    {
        var length = Flat(from, to);
        for (var s = length; s > 0f; s -= PathSample)
            if (depth(PointAt(from, to, s, length)) > -clearOf) return MathF.Min(length, s + PathSample);
        return 0f;
    }

    private static float? FirstNearer(Vector3 from, Vector3 to, Func<Vector3, float> depth, float deeperThan)
    {
        var length = Flat(from, to);
        for (var s = 0f; ; s = MathF.Min(s + PathSample, length))
        {
            if (depth(PointAt(from, to, s, length)) > deeperThan) return s;
            if (s >= length) return null;
        }
    }

    private static Vector3 PointAt(Vector3 from, Vector3 to, float s, float length) => length > 0f ? Vector3.Lerp(from, to, s / length) : to;

    private static float? Earliest(float? a, float? b) => a is null ? b : b is null ? a : MathF.Min(a.Value, b.Value);

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private void MoveEveryBotToItsRewind()
    {
        awaitingPuddle.Clear();
        foreach (var role in Enum.GetValues<PartyRole>()) Send(role, RewindSpot(role), CueKind.Resolve, RewindRoom, inBy: ReturnSavedAt);
    }

    private void MoveEveryBotForSpiritTaker()
    {
        awaitingPuddle.Clear();
        var reach = (Natives.Data.Action(ActionId.SpiritTaker)?.EffectRange ?? 0f) + FragmentRadius;
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            var spot = SpiritTakerSpot(role);
            var room = Flat(spot, FragmentPosition) - reach > SpiritTakerRoom + HazardMargin ? SpiritTakerRoom : 0f;
            Send(role, spot, CueKind.Resolve, room, inBy: SpiritTakerAt);
        }
    }

    private void MoveEveryBotForAkhMorn()
    {
        foreach (var role in Enum.GetValues<PartyRole>())
            Send(role, AkhMornSpot(role), CueKind.Resolve, role == PartyRole.MainTank ? SoloAkhMornRoom : AkhMornStackRoom, inBy: AkhMornAt);
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
}
