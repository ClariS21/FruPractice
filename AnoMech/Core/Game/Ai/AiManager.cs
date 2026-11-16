using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using AnoMech.Core.Game.Party;
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
    /// <param name="arrivalTime">If not empty, this is expected arrival time for bots. They will sprint if there is not enough time to walk, and they will leave as late as possible otherwise</param>
    /// <param name="sprint">Ignored if arrivalTime is set. Sprint towards target instead of walking</param>
    /// <param name="settleFraction">With LastMoment and arrivalTime: the share of a walker's spare time spent waiting at the destination instead of the start</param>
    /// <param name="hold">With HumanLike and arrivalTime, wait until the latest safe departure</param>
    /// <param name="stagingMove">Open staging with no intervening mechanic, where profile arrival timing is safe</param>
    /// <param name="cue">Why each role goes where it does, for the death recap. Every live slot's spot goes on world.Strat, the human's included</param>
    public void Move(float time, Func<IAiMove> positions, float? jitter = null, float? arrivalTime = null, bool sprint = false,
        float settleFraction = 0f, StratCue? cue = null, bool hold = false, bool stagingMove = false,
        Func<PartyRole, float>? departureDelay = null,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        world.Departures.Add(world.Events.Elapsed + MathF.Max(0f, time));
        world.Events.Add(time, () =>
        {
            var move = positions();
            for (int i = 0; i < 8; i++)
            {
                if (move[i] is not { } local) continue;
                var member = world.Party.Get(i);
                if (member == null || !member.IsAlive()) continue;
                world.Strat.Record(new StratTarget((PartyRole)i, new Vector3(local.X, 0f, local.Y), world.Events.Elapsed, startedAt + arrivalTime,
                    cue?.Mechanic, cue?.Why((PartyRole)i), cue?.Source));
                var target = Jitter(new Vector3(local.X, 0f, local.Y), jitter ?? (world.BotTiming == BotTiming.HumanLike ? HumanLikeJitter : LastMomentJitter));
                var partyMember = member as ISimPartyMember;
                var role = partyMember?.Role.ToString() ?? $"slot{i}";
                var dist = Vector2.Distance(new Vector2(member.Position.X, member.Position.Z), new Vector2(target.X, target.Z));
                var (sprinting, delay) = arrivalTime is { } arrival
                    ? PlanArrival(dist, arrival - time - MoveDeadlineSafetyMargin)
                    : (sprint, 0f);
                if (departureDelay is not null)
                {
                    var requested = MathF.Max(0f, departureDelay((PartyRole)i));
                    delay = arrivalTime is not null
                        ? MathF.Min(MathF.Max(0f, delay - (StaggeredArrivalMargin - MoveDeadlineSafetyMargin)), requested)
                        : requested;
                }
                else if (world.BotTiming == BotTiming.HumanLike && arrivalTime is not null && !hold)
                {
                    var reaction = MathF.Max(0.15f, world.AiReactionBase((PartyRole)i) + (world.Stream("ai-reaction").NextSingle() - 0.5f) * 0.5f);
                    if (stagingMove && member is SimPartyNpc && arrivalTime is { } deadline && deadline - time >= 1f)
                    {
                        var profile = world.BotProfile((PartyRole)i);
                        var adjustedReaction = reaction * profile.ReactionMultiplier + profile.Lag;
                        (sprinting, delay) = PlanHumanLike(profile, dist, startedAt + deadline - world.Events.Elapsed,
                            adjustedReaction, world.Stream($"bot-arrival-{i}"));
                    }
                    else
                        delay = MathF.Min(delay, reaction);
                }
                else if (world.BotTiming == BotTiming.HumanLike && arrivalTime is null && !hold && stagingMove && member is SimPartyNpc)
                {
                    var profile = world.BotProfile((PartyRole)i);
                    var reaction = MathF.Max(0.15f, world.AiReactionBase((PartyRole)i) + (world.Stream("ai-reaction").NextSingle() - 0.5f) * 0.5f);
                    delay = reaction * profile.ReactionMultiplier + profile.Lag;
                }
                else if (world.BotTiming == BotTiming.LastMoment && !hold)
                    delay *= 1f - Math.Clamp(settleFraction, 0f, 1f);
                var speed = sprinting ? SprintSpeed : RunSpeed;

                if (sprinting) partyMember?.UseSprint(dist / SprintSpeed + 1f);
                // `for`: the spot before the jitter, which each client rolls on its own stream.
                AnoMech.Core.DiagnosticLog.Info($"[AiManager] Move@{time:F1}: {role} ({member.Position.X:F1},{member.Position.Z:F1}) -> ({target.X:F1},{target.Z:F1}) for ({local.X:F2},{local.Y:F2}) {dist:F1}y{(sprinting ? " sprinting" : "")}{(delay > 0f ? $", leaving in {delay:F2}s" : "")}.");
                if (delay > 0f) world.Departures.Add(world.Events.Elapsed + delay, (PartyRole)i);
                if (delay > 0f) world.Events.Add(delay, () => member.MoveTo(target, speed: speed), file, line);
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

    internal static (bool Sprint, float Delay) PlanHumanLike(BotMovementProfile profile, float dist, float available,
        float reaction, Rng rng)
    {
        var margin = profile.ArrivalMargin(rng);
        var latestWalk = available - margin - dist / RunSpeed;
        if (latestWalk < 0f || (profile.Style != BotArrivalStyle.Early && latestWalk < reaction))
            return (true, 0f);
        if (profile.Style == BotArrivalStyle.Early)
            return (false, MathF.Min(reaction, latestWalk));
        return (false, latestWalk);
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
