using System;
using System.Collections.Generic;
using System.Numerics;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Ai.Habits;

// One uptime move a bot decided on: where the strat's spot is, where it goes and settles, and when
// the strat next moves it.
internal sealed record UptimeMove(PartyRole Role, float At, Vector3 Anchor, Vector3 Destination, Vector3 Settle, float Room, float? NextStrat);

// What a bot does between mechanics, on its own: melee keep to their target, the rest shift now and then.
// It runs only inside windows the scenario declares with Allow, and only when the bot has stood on its strat spot for a second and no strat move is due within
// three seconds, never goes more than 2.5 y from that spot (safe by design, which is what keeps it off
// the AoEs), and never has a move still walking when the strat's next one is three seconds away.
// Its moves go through HabitMovement like a strat's, so they look the same; the main tank and the
// human's seat never move.
internal sealed class UptimeMovement(SimWorld world)
{
    // Every uptime destination and settle spot stays within this of the strat spot.
    internal const float Bound = 2.5f;
    // A destination goes up to this far, which leaves the bound's rest as the move's room.
    private const float Reach = 2.1f;
    internal const float QuietBefore = 3f;
    internal const float SettledFor = 1f;
    // A move's InBy: the habit layer is on its spot a Margin and a frame before it, so this ends the walk
    // QuietBefore ahead of the strat's next departure, give or take a frame.
    private const float InByBeforeNext = QuietBefore - HabitTiming.Margin;
    private const float MeleeReach = 3.5f;
    // Where a melee bot aims: within reach with room left for its settle.
    private const float MeleeAimMin = 0.6f;
    private const float MeleeAimMax = 3f;
    private const float MeleeAimDefault = 2.5f;
    private const float MeleePoll = 0.25f;
    private const float MeleeDrift = 1f;
    private const float MeleeGap = 1.5f;
    private const float Flank = MathF.PI / 2f;
    private const float Spread = MathF.PI / 6f;
    private const float RepositionRate = 0.5f;
    private const float MinStep = 0.7f;

    private sealed class Seat
    {
        internal float Epoch = float.NaN;
        internal float NextAt = float.PositiveInfinity;
        internal float NextSwitchAt = float.PositiveInfinity;
        internal float SwitchRate;
        internal float Rate;
        internal float CentreQuantile;
        internal float Jitter;
        // 0: rear, 1: left flank, -1: right flank.
        internal int Side;
        internal float LastMoveAt = float.NegativeInfinity;
    }

    private readonly Seat[] seats = [new(), new(), new(), new(), new(), new(), new(), new()];

    internal event Action<UptimeMove>? Issued;

    private readonly List<(float From, float To, float MaxDistance)> windows = [];

    // Declares a stretch of scenario time [from, to] where uptime may run, with its moves kept within
    // maxDistance of the strat spot (never more than Bound). Outside declared windows uptime is inert:
    // bots stand on their strat spots while hazards are live.
    internal void Allow(float from, float to, float maxDistance = Bound) => windows.Add((from, to, maxDistance));

    private bool TryWindow(float now, out float maxDistance)
    {
        maxDistance = 0f;
        var found = false;
        foreach (var (from, to, max) in windows)
        {
            if (now < from || now > to) continue;
            maxDistance = found ? MathF.Max(maxDistance, max) : max;
            found = true;
        }
        maxDistance = MathF.Min(maxDistance, Bound);
        return found;
    }

    private float Limit => TryWindow(world.Events.Elapsed, out var max) ? max : Bound;

    internal void Clear()
    {
        windows.Clear();
        for (var i = 0; i < seats.Length; i++) seats[i] = new();
    }

    // Whether the seat is in an uptime stretch and standing: eligible to start a move.
    internal bool IsActive(PartyRole role) => Eligible(role, world.Events.Elapsed, out var bot, out var anchor, out _, out _) && Applies(role, bot, anchor);

    // In an uptime stretch, standing or walking an uptime move.
    internal bool IsEngaged(PartyRole role)
        => IsActive(role) || world.Movement.PlanFor(role) is { Order.Uptime: true } && !world.Movement.IsSettled(role);

    private bool Eligible(PartyRole role, float now, out SimPartyNpc bot, out Vector3 anchor, out float settledAt, out float? due)
    {
        due = null;
        settledAt = 0f;
        anchor = default;
        bot = null!;
        if (!TryWindow(now, out _)) return false;
        if (role == PartyRole.MainTank || role == world.Party.PlayerRole) return false;
        if (world.BotTiming != BotTiming.HumanLike || world.BotCombatHalted || !world.BotCombatEnabled) return false;
        // Uptime is fighting behaviour: no target, no uptime.
        if (world.Party.Combat?.FindTarget() is null) return false;
        if (!world.Movement.TryStratAnchor(role, out bot, out anchor, out settledAt)) return false;
        if (!bot.IsAlive() || now - settledAt < SettledFor) return false;
        due = world.Departures.NextStratAfter(now, role);
        return due is null || due.Value - now >= QuietBefore;
    }

    // A melee bot only has uptime when its spot is near enough to the boss to reach it.
    private bool Applies(PartyRole role, SimPartyNpc bot, Vector3 anchor)
    {
        if (!IsMelee(role)) return true;
        return world.Party.Combat?.FindTarget() is { } boss && Flat(anchor, boss.Position) <= boss.HitboxRadius + MeleeAimMax + Reach;
    }

    private static bool IsMelee(PartyRole role) => SimWorld.HabitRoleOf(role) == HabitRole.Melee;

    internal void Tick()
    {
        var now = world.Events.Elapsed;
        for (var i = 0; i < seats.Length; i++)
        {
            var role = (PartyRole)i;
            if (!Eligible(role, now, out var bot, out var anchor, out var settledAt, out var due)) continue;
            var seat = seats[i];
            if (!Applies(role, bot, anchor)) continue;
            var metrics = world.HabitFor(role)!.Metrics;
            var rng = world.Movement.UptimeStream(role);
            if (seat.Epoch != settledAt) Begin(seat, role, settledAt, now, metrics, rng);
            if (now < seat.NextAt) continue;
            if (bot.Relocating || bot.MovementLocked || world.Party.Combat?.IsCasting(bot) != false) continue;

            if (IsMelee(role)) TickMelee(seat, role, bot, anchor, due, now, metrics, rng);
            else TickReposition(seat, role, bot, anchor, due, now, rng);
        }
    }

    private void Begin(Seat seat, PartyRole role, float settledAt, float now, HabitMetrics metrics, Rng rng)
    {
        seat.Epoch = settledAt;
        if (IsMelee(role))
        {
            seat.CentreQuantile = rng.NextSingle();
            seat.Side = rng.NextSingle() < 0.5f ? 0 : rng.NextSingle() < 0.5f ? 1 : -1;
            seat.Jitter = (rng.NextSingle() * 2f - 1f) * Spread;
            seat.SwitchRate = metrics.SwitchRate?.Sample(rng) ?? 0f;
            seat.NextSwitchAt = now + Wait(rng, seat.SwitchRate);
            seat.NextAt = now;
            return;
        }
        // A profile that stands still through its casts moves seldom.
        seat.Rate = RepositionRate * (1f - Math.Clamp(metrics.CastStill?.Sample(rng) ?? 1f, 0f, 1f));
        seat.NextAt = now + Wait(rng, seat.Rate);
    }

    private void TickMelee(Seat seat, PartyRole role, SimPartyNpc bot, Vector3 anchor, float? due, float now, HabitMetrics metrics, Rng rng)
    {
        seat.NextAt = now + MeleePoll;
        if (world.Party.Combat?.FindTarget() is not { } boss) return;
        if (now >= seat.NextSwitchAt)
        {
            // Rear and flank take turns; a flank is either side.
            seat.Side = seat.Side == 0 ? (rng.NextSingle() < 0.5f ? 1 : -1) : 0;
            seat.Jitter = (rng.NextSingle() * 2f - 1f) * Spread;
            seat.NextSwitchAt = now + Wait(rng, seat.SwitchRate);
        }
        var hitbox = boss.HitboxRadius;
        var centre = metrics.CentreDistance.TryGetValue(boss.DisplayName, out var q) ? q.At(seat.CentreQuantile) : hitbox + MeleeAimDefault;
        centre = Math.Clamp(centre, hitbox + MeleeAimMin, hitbox + MeleeAimMax);
        var bearing = boss.Rotation + seat.Side * Flank + (seat.Side == 0 ? MathF.PI : 0f) + seat.Jitter;
        var wanted = boss.Position + new Vector3(MathF.Sin(bearing), 0f, MathF.Cos(bearing)) * centre;
        var spot = Within(anchor + Flat3(wanted - anchor), anchor, MathF.Min(Reach, Limit));
        spot.Y = anchor.Y;
        if (Flat(spot, boss.Position) > hitbox + MeleeAimMax + 0.05f) return;
        var walk = Flat(bot.Position, spot);
        var outOfReach = Flat(bot.Position, boss.Position) > hitbox + MeleeReach - 0.2f;
        if (now - seat.LastMoveAt < MeleeGap || walk <= (outOfReach ? 0.15f : MeleeDrift)) return;
        if (!Issue(seat, role, bot, anchor, spot, due, now)) seat.NextAt = now + 0.5f;
    }

    private void TickReposition(Seat seat, PartyRole role, SimPartyNpc bot, Vector3 anchor, float? due, float now, Rng rng)
    {
        seat.NextAt = now + Wait(rng, seat.Rate);
        var reach = MathF.Min(Reach, Limit);
        var near = MathF.Min(0.8f, reach);
        var radius = near + (reach - near) * rng.NextSingle();
        var turn = rng.NextSingle() * MathF.Tau;
        var spot = anchor + new Vector3(MathF.Cos(turn), 0f, MathF.Sin(turn)) * radius;
        // A step too small to read as one goes to the other side of the spot.
        if (Flat(bot.Position, spot) < MinStep) spot = anchor + (anchor - spot);
        if (!Issue(seat, role, bot, anchor, spot, due, now)) seat.NextAt = now + 0.5f;
    }

    private bool Issue(Seat seat, PartyRole role, SimPartyNpc bot, Vector3 anchor, Vector3 spot, float? due, float now)
    {
        spot.Y = anchor.Y;
        var room = MathF.Max(0f, Limit - Flat(spot, anchor));
        var order = new HabitOrder(spot, room, now, CueKind.Cast, due - InByBeforeNext, null, null, null, false);
        if (!world.Movement.PlanUptime(bot, role, order, nameof(UptimeMovement), 0)) return false;
        seat.LastMoveAt = now;
        var plan = world.Movement.PlanFor(role)!;
        DiagnosticLog.Info($"[Uptime] {role}: ({bot.Position.X:F1},{bot.Position.Z:F1}) -> ({spot.X:F1},{spot.Z:F1}) within {Flat(spot, anchor):F1}y of ({anchor.X:F1},{anchor.Z:F1}), settling at ({plan.Target.X:F1},{plan.Target.Z:F1}), room {room:F1}.");
        Issued?.Invoke(new UptimeMove(role, now, anchor, spot, plan.Target, room, due));
        return true;
    }

    // Exponential: the gaps of events at `rate` per second.
    private static float Wait(Rng rng, float rate) => rate > 0f ? -MathF.Log(1f - rng.NextSingle()) / rate : float.PositiveInfinity;

    private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Z), new Vector2(b.X, b.Z));

    private static Vector3 Flat3(Vector3 v) => new(v.X, 0f, v.Z);

    private static Vector3 Within(Vector3 point, Vector3 centre, float radius)
    {
        var offset = Flat3(point - centre);
        return offset.LengthSquared() > radius * radius ? centre + Vector3.Normalize(offset) * radius : point;
    }
}
