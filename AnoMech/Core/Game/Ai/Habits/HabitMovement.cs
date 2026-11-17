using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Ai.Habits;

// One habit-timed strat move for one bot, in EventScheduler seconds.
internal readonly record struct HabitOrder(Vector3 Spot, float Room, float KnownAt, CueKind KnownBy,
    float? InBy, float? OutBy, float? ExitDistance, float? NotBefore, bool Sprint, bool Uptime = false, float? Facing = null);

// A seat's current habit move: the spot it settles on, and when it sets off.
internal sealed record HabitPlan(int Generation, HabitOrder Order, Vector3 Target, float Departure, float Speed);

// Carries out habit-timed strat moves of bots: the departure from the profile's reaction and share of
// the slack, then a path shaped like a person's (one sideways bulge, a pause, an overshoot), a settle
// spot off the exact one, and idle shuffles while the bot waits. A seat's generation drops the
// delayed legs of a move that a newer order replaced.
internal sealed class HabitMovement(SimWorld world)
{
    // Events fire on the first frame at or after their time and a walk ends on a frame, so a plan
    // keeps this much in hand beyond the margin, and each extra leg of a shaped path LegReserve more.
    private const float FrameReserve = 0.1f;
    private const float LegReserve = 0.05f;
    // A shorter trip goes straight: a curve, pause or overshoot on a short step reads as a twitch.
    private const float ShapedTrip = 2f;
    private const float MaxBulge = 1.5f;
    private const float MaxPause = 0.6f;
    private const float StandBeforeIdle = 1.5f;
    private const float IdleClearOfDeparture = 1f;

    private sealed class Seat
    {
        internal int Generation;
        internal HabitPlan? Plan;
        internal SimPartyNpc? Bot;
        internal HabitMetrics? Metrics;
        internal bool Settled;
        internal float IdleRate;
        internal float NextIdleAt = float.PositiveInfinity;
        // The last strat move's plan, which an uptime move leaves standing as the anchor, and when it settled.
        internal HabitPlan? StratPlan;
        internal float StratSettledAt = float.NaN;
        // What an uptime move in flight replaced, for when it is given up.
        internal HabitPlan? Prior;
    }

    // A deferred uptime departure is retried this often, and given up past its deadline.
    private const float UptimeRetry = 0.1f;

    private readonly Seat[] seats = [new(), new(), new(), new(), new(), new(), new(), new()];
    private static readonly string[] Streams = Enum.GetValues<PartyRole>().Select(role => $"movement-habit/{role}").ToArray();
    // Uptime draws on its own streams, so it never shifts the shape of a strat move.
    private static readonly string[] UptimeStreams = Enum.GetValues<PartyRole>().Select(role => $"movement-uptime/{role}").ToArray();

    internal HabitPlan? PlanFor(PartyRole role) => seats[(int)role].Plan;

    internal bool IsSettled(PartyRole role) => seats[(int)role].Settled;

    // The seat's last strat move, once the bot has settled on it: the bot, the spot the strat gave it
    // and when it got there. False while a strat move is walking, for the human's seat and for a move
    // that wasn't habit-timed. An uptime move settling doesn't change the answer, but while one walks
    // the bot isn't settled.
    internal bool TryStratAnchor(PartyRole role, out SimPartyNpc bot, out Vector3 anchor, out float settledAt)
    {
        var seat = seats[(int)role];
        bot = seat.Bot!;
        anchor = seat.StratPlan?.Order.Spot ?? default;
        settledAt = seat.StratSettledAt;
        return seat.Settled && seat.Bot is not null && seat.StratPlan is not null && !float.IsNaN(seat.StratSettledAt);
    }

    // The seat's own uptime stream.
    internal Rng UptimeStream(PartyRole role) => world.Stream(UptimeStreams[(int)role]);

    internal void Clear()
    {
        foreach (var seat in seats) Reset(seat);
    }

    // A new strat order for the seat, of either kind: the habit move it replaces stops, idling too.
    internal int Supersede(PartyRole role)
    {
        var seat = seats[(int)role];
        Reset(seat);
        return seat.Generation;
    }

    internal bool Current(PartyRole role, int generation) => seats[(int)role].Generation == generation;

    private static void Reset(Seat seat)
    {
        seat.Generation++;
        seat.Plan = null;
        seat.Bot = null;
        seat.Metrics = null;
        seat.Settled = false;
        seat.NextIdleAt = float.PositiveInfinity;
        seat.StratPlan = null;
        seat.StratSettledAt = float.NaN;
        seat.Prior = null;
    }

    private Rng RngFor(PartyRole role, bool uptime) => world.Stream((uptime ? UptimeStreams : Streams)[(int)role]);

    // An uptime move of a settled bot: no new generation, so the strat move stays the anchor. False
    // (and nothing planned) when the order can't be walked on time, which for uptime means it would
    // need to sprint, or arrive after its InBy.
    internal bool PlanUptime(SimPartyNpc bot, PartyRole role, HabitOrder order, string file, int line)
    {
        var seat = seats[(int)role];
        return Plan(bot, role, seat.Generation, order with { Uptime = true, Facing = bot.Rotation }, file, line);
    }

    internal bool Plan(SimPartyNpc bot, PartyRole role, int generation, HabitOrder order, string file, int line)
    {
        var profile = world.HabitFor(role)!;
        var metrics = profile.Metrics;
        var rng = RngFor(role, order.Uptime);
        var now = world.Events.Elapsed;
        var target = order.Spot + Offset(rng.NextSingle(), MathF.Min(metrics.Settle!.Sample(rng), order.Room));
        // A walk still in progress may carry the bot as far as where it is heading before it sets off.
        var distance = Flat(bot.Position, target);
        if (bot.MoveDestination is { } heading) distance = MathF.Max(distance, Flat(heading, target));
        var quantile = rng.NextSingle();
        var reaction = metrics.Reaction(order.KnownBy).At(quantile);
        var inBy = order.InBy - FrameReserve;
        var outBy = order.OutBy - FrameReserve;
        var depart = HabitTiming.Departure(now, order.KnownAt, reaction, quantile, metrics.SlackUse!.Sample(rng),
            distance / HabitTiming.Walk, inBy, outBy, order.ExitDistance, order.NotBefore);
        var walkIsLate = now + distance / HabitTiming.Walk > inBy - HabitTiming.Margin
                         || now + (order.ExitDistance ?? distance) / HabitTiming.Walk > outBy - HabitTiming.Margin;
        var speed = walkIsLate || order.InBy is null && order.Sprint ? AiManager.SprintSpeed : HabitTiming.Walk;
        if (order.NotBefore is { } entry) depart = MathF.Max(depart, entry - distance / speed);
        // Uptime never sprints, and never walks into the strat's next move.
        if (order.Uptime && (speed > HabitTiming.Walk || depart + distance / speed > inBy - HabitTiming.Margin)) return false;

        var plan = new HabitPlan(generation, order, target, depart, speed);
        var seat = seats[(int)role];
        if (order.Uptime) seat.Prior = seat.Plan;
        seat.Plan = plan;
        seat.Bot = bot;
        seat.Metrics = metrics;
        if (order.Uptime)
        {
            seat.Settled = false;
            seat.NextIdleAt = float.PositiveInfinity;
        }
        else seat.StratPlan = plan;
        world.Departures.Add(depart, role, order.Uptime);
        DiagnosticLog.Info($"[AiManager] Habit move: {role} as {profile.Id} ({bot.Position.X:F1},{bot.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) for ({order.Spot.X:F2},{order.Spot.Z:F2}) {distance:F1}y{(speed > HabitTiming.Walk ? " sprinting" : "")}, known {order.KnownAt:F2} + {reaction:F2}s by {order.KnownBy}, leaving at {depart:F2}.");
        world.Events.Add(depart - now, () => Depart(bot, role, plan, file, line), file, line);
        return true;
    }

    // The path is laid out from where the bot actually stands when it sets off.
    private void Depart(SimPartyNpc bot, PartyRole role, HabitPlan plan, string file, int line)
    {
        var seat = seats[(int)role];
        if (seat.Generation != plan.Generation || !bot.IsAlive()) return;
        var order = plan.Order;
        var now = world.Events.Elapsed;
        if (order.Uptime && (bot.Casting.IsCasting || world.Party.Combat?.IsCasting(bot) == true))
        {
            // A bar went up in the reaction time: uptime waits it out, or gives the move up.
            if (plan.Order.InBy is { } by && now + UptimeRetry + Vector3.Distance(bot.Position with { Y = plan.Target.Y }, plan.Target) / plan.Speed > by - FrameReserve - HabitTiming.Margin)
                AbandonUptime(role, plan);
            else
            {
                world.Departures.Add(now + UptimeRetry, role, true);
                world.Events.Add(UptimeRetry, () => Depart(bot, role, plan, file, line), file, line);
            }
            return;
        }
        var speed = plan.Speed;
        var from = bot.Position with { Y = plan.Target.Y };
        var length = Vector3.Distance(from, plan.Target);
        if (order.NotBefore is { } entry && now + length / speed < entry - 0.02f)
        {
            var wait = entry - length / speed - now;
            world.Departures.Add(now + wait, role, order.Uptime);
            world.Events.Add(wait, () => Depart(bot, role, plan, file, line), file, line);
            return;
        }

        var rng = RngFor(role, order.Uptime);
        var metrics = seat.Metrics!;
        // Every roll is drawn whether it is used or not, so one move's shape never shifts the next one's.
        var along = 0.4f + 0.2f * rng.NextSingle();
        var side = rng.NextSingle() < 0.5f ? -1f : 1f;
        var bulge = MathF.Min(MathF.Min(metrics.Bulge!.Sample(rng) * length, order.Room), MaxBulge);
        var pauses = rng.NextSingle() < metrics.PauseChance!.Sample(rng);
        var pause = MathF.Min(metrics.Pause!.Sample(rng), MaxPause);
        var overshoots = rng.NextSingle() < metrics.OvershootChance!.Sample(rng);
        var overshoot = MathF.Min(metrics.Overshoot!.Sample(rng), order.Room);
        var stepBackAfter = 0.3f + 0.3f * rng.NextSingle();

        // What a detour, a stop or an overshoot may spend and still leave the margin.
        var budget = MathF.Min(
            order.InBy - FrameReserve - HabitTiming.Margin - now - length / speed ?? float.PositiveInfinity,
            order.OutBy - FrameReserve - HabitTiming.Margin - now - (order.ExitDistance ?? length) / speed ?? float.PositiveInfinity);
        var shaped = length >= ShapedTrip;
        var direction = length > 0f ? (plan.Target - from) / length : Vector3.Zero;
        var waypoint = Vector3.Lerp(from, plan.Target, along);
        var bent = waypoint + new Vector3(-direction.Z, 0f, direction.X) * (bulge * side);
        var bendCost = (Vector3.Distance(from, bent) + Vector3.Distance(bent, plan.Target) - length) / speed + LegReserve;
        var bends = shaped && bulge > 0.05f && bendCost <= budget;
        if (bends)
        {
            waypoint = bent;
            budget -= bendCost;
        }
        var pauseCost = pause + (bends ? 0f : LegReserve);
        var stops = shaped && pauses && pause > 0f && pauseCost <= budget;
        if (stops) budget -= pauseCost;
        var lastFrom = bends || stops ? waypoint : from;
        var lastLength = Vector3.Distance(lastFrom, plan.Target);
        var beyond = lastLength > 0f
            ? Clamp(plan.Target + (plan.Target - lastFrom) / lastLength * overshoot, order.Spot, order.Room)
            : plan.Target;
        var back = Vector3.Distance(beyond, plan.Target);
        var overshootsHere = shaped && overshoots && back > 0.05f && 2f * back / speed + stepBackAfter + 2f * LegReserve <= budget;

        var cursor = 0f;
        if (bends || stops)
        {
            Leg(0f, waypoint);
            cursor = Vector3.Distance(from, waypoint) / speed + LegReserve + (stops ? pause : 0f);
        }
        var end = overshootsHere ? beyond : plan.Target;
        Leg(cursor, end, !overshootsHere);
        cursor += Vector3.Distance(lastFrom, end) / speed + LegReserve;
        if (overshootsHere)
        {
            cursor += stepBackAfter;
            Leg(cursor, plan.Target, true);
            cursor += back / speed + LegReserve;
        }
        if (speed > HabitTiming.Walk) bot.UseSprint(cursor + 1f);
        world.Events.Add(cursor, () => Settle(role, plan), file, line);

        // A leg ordered during a knockback is walked once it ends, as an order of the old path is.
        void Leg(float delay, Vector3 to, bool last = false)
        {
            // An uptime move gives the bot its facing back on arrival; the strat sets facing itself.
            float? facing = last ? order.Facing : null;
            if (delay <= 0f)
            {
                bot.MoveTo(to, speed: speed, finalRotation: facing);
                return;
            }
            world.Departures.Add(now + delay, role, order.Uptime);
            world.Events.Add(delay, () =>
            {
                if (Current(role, plan.Generation) && bot.IsAlive()) bot.MoveTo(to, speed: speed, finalRotation: facing);
            }, file, line);
        }
    }

    // An uptime move that can no longer set off in time: the seat is as it was before it.
    private void AbandonUptime(PartyRole role, HabitPlan plan)
    {
        var seat = seats[(int)role];
        if (seat.Generation != plan.Generation || seat.Prior is null) return;
        seat.Plan = seat.Prior;
        seat.Settled = true;
        seat.NextIdleAt = world.Events.Elapsed + StandBeforeIdle;
    }

    private void Settle(PartyRole role, HabitPlan plan)
    {
        var seat = seats[(int)role];
        if (seat.Generation != plan.Generation) return;
        var rng = RngFor(role, plan.Order.Uptime);
        seat.Settled = true;
        if (!plan.Order.Uptime) seat.StratSettledAt = world.Events.Elapsed;
        seat.IdleRate = seat.Metrics!.IdleRate!.Sample(rng);
        seat.NextIdleAt = world.Events.Elapsed + StandBeforeIdle + Wait(rng, seat.IdleRate);
    }

    // Idle shuffles: small steps at the profile's rate while the bot stands with nothing due soon,
    // never off the room around its spot, and never across a cast.
    internal void Tick()
    {
        if (world.BotTiming != BotTiming.HumanLike || world.BotCombatHalted) return;
        var now = world.Events.Elapsed;
        for (var i = 0; i < seats.Length; i++)
        {
            var seat = seats[i];
            if (!seat.Settled || now < seat.NextIdleAt || seat.Bot is not { } bot || seat.Plan is not { } plan) continue;
            if (!bot.IsAlive() || bot.Relocating || bot.MovementLocked || bot.Casting.IsBusy) continue;
            var role = (PartyRole)i;
            var rng = world.Stream(Streams[(int)role]);
            var size = seat.Metrics!.IdleStep!.Sample(rng);
            var angle = rng.NextSingle();
            seat.NextIdleAt = now + Wait(rng, seat.IdleRate);
            var room = plan.Order.Room;
            var here = bot.Position with { Y = plan.Target.Y };
            if (Vector3.Distance(here, plan.Target) > room + 0.01f) continue;
            var to = Clamp(Clamp(here + Offset(angle, size), plan.Target, room), plan.Order.Spot, room);
            var seconds = Vector3.Distance(here, to) / HabitTiming.Walk;
            if (seconds < 0.01f) continue;
            if (world.Departures.NextAfter(now, role) is { } due && due - now < MathF.Max(StandBeforeIdle, seconds + IdleClearOfDeparture)) continue;
            bot.MoveTo(to, speed: HabitTiming.Walk);
        }
    }

    // Exponential: the gaps of steps taken at `rate` per second.
    private static float Wait(Rng rng, float rate) => rate > 0f ? -MathF.Log(1f - rng.NextSingle()) / rate : float.PositiveInfinity;

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    internal static Vector3 Clamp(Vector3 point, Vector3 centre, float room)
    {
        var offset = point - centre;
        return offset.LengthSquared() > room * room ? centre + Vector3.Normalize(offset) * room : point;
    }

    // `turn` in [0, 1) of a full circle.
    private static Vector3 Offset(float turn, float radius)
    {
        var angle = turn * MathF.Tau;
        return new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius);
    }
}
