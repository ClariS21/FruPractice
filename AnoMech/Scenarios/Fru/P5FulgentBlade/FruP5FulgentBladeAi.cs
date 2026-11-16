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
    private const float PersonalMargin = 0.45f;
    private const float StepAfterWave = 0.3f;

    public void Run(FruP5FulgentBladeState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        var rng = world.Stream("p5-fulgent-movement");
        // Spot 0 waits out waves 0-1, spot j (1-3) holds waves 2j and 2j+1.
        var spots = PlanSpots(state);
        var offsets = Enumerable.Range(0, 8).Select(_ => Offset(0.3f + rng.NextSingle() * 0.2f, rng.NextSingle())).ToArray();
        var drifts = new[] { 18.2f, 19.5f }
            .Select(_ => Enumerable.Range(0, 8).Select(_ => Offset(0.1f + rng.NextSingle() * 0.15f, rng.NextSingle())).ToArray()).ToArray();
        // Each bot's personal spot, pulled back towards the shared one until it keeps clear of the lines.
        Vector3 Personal(int spot, PartyRole role, Vector3? drift = null)
        {
            var wanted = offsets[(int)role] + (drift ?? Vector3.Zero);
            for (var scale = 1f; scale > 0f; scale -= 0.25f)
            {
                var point = spots[spot] + wanted * scale;
                if (Margin(state, point, WavesHeldAt(spot)) >= PersonalMargin) return point;
            }
            return spots[spot];
        }

        ai.Move(0f, () => ByRole(CastPosition), jitter: 0f);
        ai.Move(13.2f, () => ByRole(role => Personal(0, role)), jitter: 0f, arrivalTime: 17.2f,
            departureDelay: role => role.IsTank() ? 0f : 0.25f + (int)role * 0.08f);
        for (var i = 0; i < 2; i++)
        {
            var drift = drifts[i];
            var at = i == 0 ? 18.2f : 19.5f;
            ai.Move(at, () => ByRole(role => Personal(0, role, drift[(int)role])), jitter: 0f, arrivalTime: at + 0.5f,
                departureDelay: _ => 0f);
        }
        for (var step = 1; step <= 3; step++)
        {
            var spot = step;
            var freeingWave = 2 * step - 1;
            var leave = LastHitOfWave(freeingWave) + StepAfterWave;
            var arrive = FirstHitOfWave(freeingWave + 1) - 0.2f;
            // Leaders walk off on the freeing wave's heels. The rest sprint a beat behind them, one after
            // another within 0.2 s: a walk would leave them less than 0.8 s clear of the next wave's lines.
            var followers = 0;
            for (var i = 0; i < 8; i++)
            {
                var role = (PartyRole)i;
                var leader = IsLeader(role);
                var lag = leader ? 0f : 0.03f + 0.035f * followers++ + rng.NextSingle() * 0.025f;
                IAiMove Spot() => ByRole(r => r == role ? Personal(spot, r) : null);
                if (leader) ai.Move(leave, Spot, jitter: 0f, arrivalTime: arrive, departureDelay: _ => 0f);
                else ai.Move(leave + lag, Spot, jitter: 0f, sprint: true);
            }
        }
        ai.Move(31.9f, () => ByRole(LightPartyStack), arrivalTime: 34f);
    }

    private static bool IsLeader(PartyRole role) => role is PartyRole.MainTank or PartyRole.OffTank or PartyRole.CasterDps;

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

    private static Vector3 Offset(float radius, float angle)
        => new(radius * MathF.Cos(angle * 2f * MathF.PI), 0f, radius * MathF.Sin(angle * 2f * MathF.PI));

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
