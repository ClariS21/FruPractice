using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P5FulgentBlade;

public sealed class FruP5FulgentBladeAi : IScenarioAi<FruP5FulgentBladeState>
{
    public string Name => "Standard";

    private const float FruSimUnitsToYalms = 5f / 11.855f;

    private FruP5FulgentBladeState state = null!;

    public void Run(FruP5FulgentBladeState stateParam, SimWorld world)
    {
        state = stateParam;
        var ai = new AiManager(world);
        var rng = world.Stream("p5-fulgent-movement");
        var offsets = Enumerable.Range(0, 8).Select(_ => Offset(0.3f + rng.NextSingle() * 0.2f, rng.NextSingle())).ToArray();
        ai.Move(0f, () => ByRole(CastPosition), jitter: 0f);
        ai.Move(13.2f, () => Stack(0, offsets), jitter: 0f, arrivalTime: 17.2f,
            departureDelay: role => role.IsTank() ? 0f : 0.25f + (int)role * 0.08f);
        foreach (var at in new[] { 18.2f, 19.5f })
        {
            var drift = Enumerable.Range(0, 8).Select(_ => Offset(0.1f + rng.NextSingle() * 0.15f, rng.NextSingle())).ToArray();
            ai.Move(at, () => Stack(0, offsets, drift), jitter: 0f, arrivalTime: at + 0.5f,
                departureDelay: _ => 0f);
        }
        for (var step = 1; step < 6; step++)
        {
            var dodge = step;
            var delays = Enumerable.Range(0, 8).Select(i => i is 0 or 1 or 7
                ? rng.NextSingle() * 0.2f : 0.65f + rng.NextSingle() * 0.65f).ToArray();
            ai.Move(FruP5FulgentBladeState.HitAt(0, step) + 0.15f,
                () => Stack(dodge, offsets), jitter: 0f, arrivalTime: BeforeStripsRoll(dodge),
                departureDelay: role => delays[(int)role]);
        }
        ai.Move(31.9f, () => ByRole(LightPartyStack), arrivalTime: 34f);
    }

    private static float BeforeStripsRoll(int dodge) => FruP5FulgentBladeState.HitAt(0, dodge + 1) - 0.2f;

    private static readonly Vector3 MainTankNorthOfBoss = new(0f, 0f, -6f);

    private static Vector3? CastPosition(PartyRole role) => role switch
    {
        PartyRole.MainTank or PartyRole.OffTank => MainTankNorthOfBoss,
        PartyRole.CasterDps => new Vector3(0f, 0f, 11f),
        _ => new Vector3(0f, 0f, 5.5f),
    };

    private IAiMove Stack(int step, Vector3[] offsets, Vector3[]? drift = null)
        => ByRole(role => Dodge(step) + offsets[(int)role] + (drift is null ? Vector3.Zero : drift[(int)role]));

    private static Vector3 Offset(float radius, float angle)
        => new(radius * MathF.Cos(angle * 2f * MathF.PI), 0f, radius * MathF.Sin(angle * 2f * MathF.PI));

    private Vector3 Dodge(int step)
    {
        var offset = step switch
        {
            0 or 5 => new Vector3(-1.15f, 0f, -2.77f),
            1 or 4 => new Vector3(1.15f, 0f, 2.77f),
            2 => new Vector3(-2.77f, 0f, 1.15f),
            _ => new Vector3(2.77f, 0f, -1.15f),
        };
        return state.Place(state.Pocket + Vector3.Transform(offset * FruSimUnitsToYalms, Quaternion.CreateFromAxisAngle(Vector3.UnitY, state.Rotation)));
    }

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
