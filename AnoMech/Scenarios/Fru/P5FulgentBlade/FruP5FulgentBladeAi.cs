using System;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P5FulgentBlade;

public sealed class FruP5FulgentBladeAi : IScenarioAi<FruP5FulgentBladeState>
{
    public string Name => "Standard";

    private const float FruSimUnitsToYalms = 5f / 11.855f;

    private FruP5FulgentBladeState state = null!;

    // Waves are the hits that land together: wave n is hit n of group 0, hit n-2 of group 1, hit n-4
    // of group 2. The strat holds one spot for two waves, then steps after the odd one.
    private const int WaveCount = 8;
    private const float WaitMargin = 1.3f;
    // The walking pace AiManager plans a habit move with.
    private const float Walk = 6.5f;
    // A hit's area has decided who dies at its snapshot, so a spot is safe again just after it.
    private const float AfterSnapshot = 0.05f;
    // The scenario picks the Akh Morn targets then, from where everyone stands.
    private const float AkhMornPickAt = 34.05f;

    // Uptime between the opening cast and the first strat move: nothing is live until the first group's
    // strips fire at 18.25, and the party holds its cast spots until the spot-0 move departs.
    internal static readonly (float From, float To, float Max)[] UptimeWindows = [(1f, 8.6f, 2.5f)];

    public void Run(FruP5FulgentBladeState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        // Spot 0 waits out waves 0-1, spot j (1-3) holds waves 2j and 2j+1.
        var spots = PlanSpots(state);
        var startedAt = world.Events.Elapsed;
        foreach (var (from, to, max) in UptimeWindows) world.Uptime.Allow(startedAt + from, startedAt + to, max);

        // The strat's spots are exact for whoever the habit layer doesn't drive (LastMoment bots, and the
        // human's seat when a test drives it), so those moves say jitter: 0. Habit-timed bots settle
        // within `room` of the spot instead.
        // Opening spot: open staging.
        ai.Move(0f, () => ByRole(CastPosition), stagingMove: true, room: 1f);
        // The first group's lines are drawn at 11.55 and spot 0 follows from them alone. The danger is
        // that group's first hit.
        ai.Move(11.55f, () => ByRole(_ => spots[0]), jitter: 0f, knownAt: 11.55f, knownBy: CueKind.Cast, room: RoomAt(state, spots, 0),
            arrivalTime: FirstHitOfWave(0));
        // Each step follows the freeing wave landing (CueKind.Resolve). The spot being left is inside
        // the next wave's lines, dangerous from that wave's first hit; the walk out of them, plus the
        // room the party stood off its spot, is what it must clear by then. The window the freeing wave
        // leaves is short, so the habit layer squeezes each bot into it in its own order: the leaders
        // go on the wave's heels and the rest sprint a beat behind.
        for (var step = 1; step <= 3; step++)
        {
            var to = spots[step];
            var freeingAt = LastHitOfWave(2 * step - 1);
            var dodge = Dodge(state, spots[step - 1], to, step, RoomAt(state, spots, step - 1), RoomAt(state, spots, step));
            ai.Move(freeingAt, () => ByRole(_ => to), jitter: 0f, knownAt: freeingAt, knownBy: CueKind.Resolve, room: RoomAt(state, spots, step),
                outBy: dodge?.OutBy, exitDistance: dodge?.Exit, notBefore: freeingAt + AfterSnapshot);
        }
        // The light party stacks are in wave 6's lines until it lands (their margin from wave 7's is
        // 0.95 y, which caps the room). The bar (26.35) announced the stack long before; the move is
        // scheduled once step 3 has run, or it would cancel it. Everyone is on it 0.8 s before the
        // targets are picked.
        ai.Move(LastHitOfWave(6), () => ByRole(LightPartyStack), jitter: 0f, knownAt: 26.35f, knownBy: CueKind.Cast, room: 0.8f,
            arrivalTime: AkhMornPickAt, notBefore: LastHitOfWave(6) + AfterSnapshot);
    }

    // How far off a spot a bot may stray: inside the margin its held waves leave it.
    private static float RoomAt(FruP5FulgentBladeState state, Vector3[] spots, int spot)
        => Math.Clamp(Margin(state, spots[spot], WavesHeldAt(spot)) - 0.05f, 0.4f, 1f);

    // When the spot being left turns dangerous (the first of the next two waves whose lines it is in)
    // and how far the bot walks, on its way to the next spot, to be out of those lines. A bot stands
    // within a room of either spot, so the walk may be that much longer or shorter; the new spot is
    // clear of both waves, so a bot never needs to walk further than to it.
    private static (float OutBy, float Exit)? Dodge(FruP5FulgentBladeState state, Vector3 from, Vector3 to, int step, float fromRoom, float toRoom)
    {
        (float OutBy, float Exit)? best = null;
        var bestLatest = float.MaxValue;
        var shortest = MathF.Max(0.3f, Vector3.Distance(from, to) - fromRoom - toRoom);
        foreach (var wave in new[] { 2 * step, 2 * step + 1 })
        {
            var inside = InsideLength(state, from, to, wave);
            if (inside <= 0f) continue;
            var by = FirstHitOfWave(wave);
            var wanted = inside + fromRoom + toRoom + 0.2f;
            var exit = MathF.Min(wanted, shortest);
            // What the walk is short of the distance wanted is paid in time: the deadline is declared early.
            var latest = by - wanted / Walk;
            if (latest >= bestLatest) continue;
            bestLatest = latest;
            best = (latest + exit / Walk, exit);
        }
        return best;
    }

    // How far along the straight walk from `from` to `to` a point is still inside a band of the wave
    // (or within a hair of it): 0 if the walk starts and stays clear.
    private static float InsideLength(FruP5FulgentBladeState state, Vector3 from, Vector3 to, int wave)
    {
        var length = Vector3.Distance(from, to);
        var last = 0f;
        for (var d = 0f; d <= length; d += 0.05f)
        {
            var point = length <= 0f ? from : Vector3.Lerp(from, to, d / length);
            if (Margin(state, point, [wave]) < 0.15f) last = d + 0.05f;
        }
        return last;
    }

    private static IEnumerable<int> WavesHeldAt(int spot) => spot == 0 ? [0, 1] : [2 * spot, 2 * spot + 1];

    internal static float LastHitOfWave(int wave)
        => Enumerable.Range(0, FruP5FulgentBladeState.GroupCount).Where(g => wave - 2 * g >= 0).Max(g => FruP5FulgentBladeState.HitAt(g, wave - 2 * g));

    internal static float FirstHitOfWave(int wave)
        => Enumerable.Range(0, FruP5FulgentBladeState.GroupCount).Where(g => wave - 2 * g >= 0).Min(g => FruP5FulgentBladeState.HitAt(g, wave - 2 * g));

    // Each seam is a line; wave n's hit of a group is, either side of the line, a band
    // [5k, 5k + 5] away from it, k = n - 2 * group (hit 0 is the 10-wide strip centred on the line).
    // The distance from the nearest band edge to the point: negative inside a band, and the worst
    // over every seam and wave.
    internal static float Margin(FruP5FulgentBladeState state, Vector3 point, IEnumerable<int> waves)
    {
        var worst = float.MaxValue;
        foreach (var wave in waves)
            for (var group = 0; group < FruP5FulgentBladeState.GroupCount; group++)
            {
                var hit = wave - 2 * group;
                if (hit < 0) continue;
                for (var seam = 0; seam < FruP5FulgentBladeState.SeamsPerGroup; seam++)
                {
                    var line = state.Seam(group, seam);
                    var along = new Vector3(MathF.Sin(line.Rotation), 0f, MathF.Cos(line.Rotation));
                    var distance = MathF.Abs(Vector3.Dot(point - line.Position, along));
                    var low = FruP5FulgentBladeState.Step * hit;
                    var high = low + FruP5FulgentBladeState.Step;
                    var clear = distance < low ? low - distance : distance > high ? distance - high : -MathF.Min(distance - low, high - distance);
                    worst = MathF.Min(worst, clear);
                }
            }
        return worst;
    }

    // The strat's spots, from the layout alone: for each pair of waves, the spot that keeps clear of
    // every line by WaitMargin and sits closest to the pocket the party starts in. That is slightly
    // away from the first glowing intersection, then the near corner of each following overlap, on
    // whichever side the first intersection puts it.
    internal static Vector3[] PlanSpots(FruP5FulgentBladeState state)
    {
        var anchor = state.Origin;
        var reach = FruConstants.Geometry.ArenaRadius - 2f;
        var spots = new Vector3[4];
        for (var spot = 0; spot < 4; spot++)
        {
            var waves = WavesHeldAt(spot).ToArray();
            foreach (var margin in new[] { WaitMargin, 1f, 0.8f, 0.6f, 0.45f })
            {
                var best = float.MaxValue;
                for (var x = -reach; x <= reach; x += 0.1f)
                    for (var z = -reach; z <= reach; z += 0.1f)
                    {
                        var point = new Vector3(x, 0f, z);
                        var distance = Vector3.DistanceSquared(point, anchor);
                        if (distance >= best || x * x + z * z > reach * reach || Margin(state, point, waves) < margin) continue;
                        best = distance;
                        spots[spot] = point;
                    }
                if (best < float.MaxValue) break;
            }
        }
        return spots;
    }

    private static readonly Vector3 MainTankNorthOfBoss = new(0f, 0f, -6f);

    private static Vector3? CastPosition(PartyRole role) => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => MainTankNorthOfBoss,
        PartyRole.CasterDps => new Vector3(0f, 0f, 11f),
        _ => new Vector3(0f, 0f, 5.5f),
    };

    private Vector3? LightPartyStack(PartyRole role)
    {
        var outward = Vector3.Normalize(state.Origin);
        var bossLeft = new Vector3(outward.Z, 0f, -outward.X);
        var side = (int)role % 2 == 0 ? -1f : 1f;
        return outward * (14.6f * FruSimUnitsToYalms) + bossLeft * (side * 9.6f * FruSimUnitsToYalms);
    }

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
