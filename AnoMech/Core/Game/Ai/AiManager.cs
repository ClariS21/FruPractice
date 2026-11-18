using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using AnoMech.Core.Game.Party;
using AnoMech.Core.Game.Ai.Habits;
using AnoMech.Core.SimObjects;
using AnoMech.Core.Native.Interfaces;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Ai;

// Drives slot-ordered party movement from a scenario's position functions.
// Owns jitter, run speed, and event scheduling. Position functions return an
// AiMove whose entries are scenario-local XZ coords — same space MoveTo
// consumes, so AiManager forwards them as-is. Eye-spawn flip and slot
// reordering are handled inside the AiMove before it reaches here.
public sealed class AiManager
{
    // Measured in-game.
    private const float RunSpeed = 6.5f;
    public const float SprintSpeed = 8.3f;
    private const float HumanLikeJitter = 1f;
    private const float LastMomentJitter = 0.3f;
    private const float DefaultRoom = 0.5f;
    // Move's deadline math leaves zero margin, and a move needing speed within a hair of RunSpeed
    // arrives short. Only ever makes arrival earlier.
    private const float MoveDeadlineSafetyMargin = 0.25f;
    // A strat's own staggered departures still arrive at least this early (user: "don't let the bots cut it close").
    internal const float StaggeredArrivalMargin = 0.8f;

    private readonly SimWorld world;
    // A strat's times count from when its AI starts, which is later than the run's start when a
    // scenario plays one slice after another.
    private readonly float startedAt;

    public AiManager(SimWorld world)
    {
        this.world = world;
        startedAt = world.Events.Elapsed;
    }

    /// <summary>Schedule bots movement</summary>
    /// <remarks>
    /// Times count from the AI's start. With HumanLike timing a bot's move is habit-timed (its seat's
    /// FFLogs profile picks the departure, path and settle spot) once the move declares when its spot
    /// became known or where its danger is: any of knownAt, a knownBy other than Cast, room, outBy,
    /// exitDistance or notBefore, or stagingMove, and the zone is calibrated for habits. Any other
    /// Human-like move leaves after a reaction, since its arrivalTime need not be a real danger
    /// deadline. hold, departureDelay, the human's seat and LastMoment timing never use habits, and a
    /// zone not calibrated for human-like movement times every move as LastMoment.
    /// </remarks>
    /// <param name="arrivalTime">If not empty, this is expected arrival time for bots. They will sprint if there is not enough time to walk, and they will leave as late as possible otherwise. A Human-like bot leaves after a reaction instead and is on its spot at least 0.8 s before it; for a habit-timed move it is inBy</param>
    /// <param name="sprint">Ignored if arrivalTime is set. Sprint towards target instead of walking</param>
    /// <param name="settleFraction">With LastMoment and arrivalTime: the share of a walker's spare time spent waiting at the destination instead of the start</param>
    /// <param name="hold">With HumanLike and arrivalTime, wait until the latest safe departure</param>
    /// <param name="stagingMove">Open staging with no intervening mechanic, where late arrivals are safe</param>
    /// <param name="cue">Why each role goes where it does, for the death recap. Every live slot's spot goes on world.Strat, the human's included</param>
    /// <param name="knownAt">When a player could first know the spot. Defaults to time</param>
    /// <param name="knownBy">What told them: the habit reaction is measured from this kind of cue</param>
    /// <param name="room">How far a bot may stray from the exact spot (settle, overshoot, bulge, idle). Defaults to 0.5 y; replaces jitter for habit-timed moves</param>
    /// <param name="outBy">When the bot's current spot turns dangerous: it is out of it at least 0.8 s before</param>
    /// <param name="exitDistance">How far the bot walks to leave that danger. Defaults to the whole walk</param>
    /// <param name="notBefore">The spot is dangerous until then: the bot doesn't arrive earlier</param>
    public void Move(float time, Func<IAiMove> positions, float? jitter = null, float? arrivalTime = null, bool sprint = false,
        float settleFraction = 0f, StratCue? cue = null, bool hold = false, bool stagingMove = false,
        Func<PartyRole, float>? departureDelay = null,
        float? knownAt = null, CueKind knownBy = CueKind.Cast, float? room = null,
        float? outBy = null, float? exitDistance = null, float? notBefore = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        var declared = stagingMove || knownAt is not null || knownBy != CueKind.Cast || room is not null
                       || outBy is not null || exitDistance is not null || notBefore is not null;
        var firesAt = world.Events.Elapsed + MathF.Max(0f, time);
        world.Departures.Add(firesAt);
        world.Events.Add(time, () =>
        {
            var move = positions();
            var lastMoment = world.BotTiming == BotTiming.LastMoment || !world.Calibration.HumanLikeMovement;
            for (int i = 0; i < 8; i++)
            {
                if (move[i] is not { } local) continue;
                var member = world.Party.Get(i);
                if (member == null || !member.IsAlive()) continue;
                var seat = (PartyRole)i;
                var spot = new Vector3(local.X, 0f, local.Y);
                world.Strat.Record(new StratTarget(seat, spot, world.Events.Elapsed, startedAt + arrivalTime,
                    cue?.Mechanic, cue?.Why(seat), cue?.Source));
                var generation = world.Movement.Supersede(seat);
                if (declared && !hold && departureDelay is null && !lastMoment && member is SimPartyNpc bot && world.HabitFor(seat) is not null)
                {
                    world.Movement.Plan(bot, seat, generation, new HabitOrder(spot, MathF.Max(0f, room ?? DefaultRoom),
                        knownAt is { } known ? startedAt + known : firesAt, knownBy, startedAt + arrivalTime,
                        startedAt + outBy, exitDistance, startedAt + notBefore, sprint), file, line);
                    continue;
                }
                var target = Jitter(spot, jitter ?? (lastMoment ? LastMomentJitter : HumanLikeJitter));
                var partyMember = member as ISimPartyMember;
                var role = partyMember?.Role.ToString() ?? $"slot{i}";
                var dist = Vector2.Distance(new Vector2(member.Position.X, member.Position.Z), new Vector2(target.X, target.Z));
                // A human-like bot keeps the habit layer's margin on a move that declares nothing.
                var margin = !lastMoment && !hold && departureDelay is null && member is SimPartyNpc
                    ? StaggeredArrivalMargin
                    : MoveDeadlineSafetyMargin;
                var (sprinting, delay) = arrivalTime is { } arrival
                    ? PlanArrival(dist, arrival - time - margin)
                    : (sprint, 0f);
                if (departureDelay is not null)
                {
                    var requested = MathF.Max(0f, departureDelay(seat));
                    delay = arrivalTime is not null
                        ? MathF.Min(MathF.Max(0f, delay - (StaggeredArrivalMargin - MoveDeadlineSafetyMargin)), requested)
                        : requested;
                }
                else if (!lastMoment && arrivalTime is not null && !hold)
                {
                    var reaction = MathF.Max(0.15f, world.AiReactionBase(seat) + (world.Stream("ai-reaction").NextSingle() - 0.5f) * 0.5f);
                    delay = MathF.Min(delay, reaction);
                }
                else if (lastMoment && !hold)
                    delay *= 1f - Math.Clamp(settleFraction, 0f, 1f);
                var speed = sprinting ? SprintSpeed : RunSpeed;

                if (sprinting) partyMember?.UseSprint(dist / SprintSpeed + 1f);
                // `for`: the spot before the jitter, which each client rolls on its own stream.
                AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) for ({local.X:F2},{local.Y:F2}) {dist:F1}y{(sprinting ? " sprinting" : "")}{(delay > 0f ? $", leaving in {delay:F2}s" : "")}.");
                if (delay > 0f) world.Departures.Add(world.Events.Elapsed + delay, seat);
                // A Human-like order that a newer move replaced before it set off is dropped; Last moment
                // keeps every order, as it always has.
                if (delay > 0f) world.Events.Add(delay, () =>
                {
                    if (lastMoment || world.Movement.Current(seat, generation)) member.MoveTo(target, speed: speed);
                }, file, line);
                else member.MoveTo(target, speed: speed);
            }
        }, file, line);
    }

    // Walk, leaving as late as still arrives in time; sprint now if walking can't make it.
    private static (bool Sprint, float Delay) PlanArrival(float dist, float available)
    {
        var slack = available - dist / RunSpeed;
        return slack >= 0f ? (false, slack) : (true, 0f);
    }

    public void Automarker(float time, Func<Dictionary<PartyRole, Sign>> mapping, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        world.Events.Add(time, () =>
        {
            world.Party.ClearMarks();
            var marks = mapping();
            AnoMech.Core.DiagnosticLog.Info($"[AiManager] Automarker@{time:F1}: [{string.Join(", ", marks.Select(kv => $"{kv.Key}={kv.Value}"))}].");
            foreach (var (role, sign) in marks)
                if (world.Party.Get(role) is { } member && member.IsAlive())
                    world.Party.Mark(sign, member);
        }, file, line);
    }

    private Vector3 Jitter(Vector3 target, float radius)
    {
        var rng = world.Stream("ai-jitter");
        var theta = rng.NextDouble() * 2.0 * Math.PI;
        var r = radius * MathF.Sqrt((float)rng.NextDouble());
        return new Vector3(
            target.X + r * MathF.Cos((float)theta),
            target.Y,
            target.Z + r * MathF.Sin((float)theta));
    }
}
