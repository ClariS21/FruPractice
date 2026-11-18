using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;
using AnoMech.Scenarios.Umad.P3BlackHole;
using static AnoMech.Scenarios.Umad.P3Full.UmadP3FullScenario;

namespace AnoMech.Scenarios.Umad.P3Full;

public sealed class UmadP3FullAi
{
    private const string YourGroup = "your group's pulls";
    private const float Tight = 0.3f;
    private const float EndRingRadius = 9f;
    private const float ClearOfTheBuster = 11f;

    private UmadP3FullState state = null!;
    private UmadP3Cast cast = null!;
    private SimWorld world = null!;

    internal void Run(SimWorld worldParam, UmadP3FullState stateParam, UmadP3Cast castParam)
    {
        world = worldParam;
        state = stateParam;
        cast = castParam;
        var ai = new AiManager(world);
        ai.Move(BridgeThunderCastAt + 0.1f, () => BusterTankHugsExdeathRestPastTheMiddle(secondHit: false), jitter: Tight, knownAt: BridgeThunderCastAt, room: 0.5f,
            arrivalTime: BridgeThunderHitsAt[0] - 0.6f,
            cue: StratCue.ForAll("Thunder III", "The first hit's tank on Exdeath, everyone else past the middle", YourGroup));
        ai.Move(BridgeThunderHitsAt[0] + 0.1f, () => BusterTankHugsExdeathRestPastTheMiddle(secondHit: true), jitter: Tight, knownAt: BridgeThunderHitsAt[0],
            knownBy: CueKind.Resolve, room: 0.5f, arrivalTime: BridgeThunderHitsAt[1] - 0.6f, outBy: BridgeThunderHitsAt[1] - 0.6f,
            cue: StratCue.ForAll("Thunder III", "The tanks trade places on Exdeath for the second hit", YourGroup));
        ai.Move(283.95f, () => OwnEighthOfARingRoundTheMiddle(0f), jitter: Tight, knownAt: BigBangCastAt, room: 1f, arrivalTime: 285.9f,
            cue: StratCue.ForAll("Big Bang", "Out of the middle, where the last Knock Down stacked", YourGroup));
        ai.Move(285.9f, () => OwnEighthOfARingRoundTheMiddle(MathF.PI / 3f), jitter: Tight, knownAt: BlizzardCastAt, room: 1f, arrivalTime: BigBangAt - 0.3f,
            cue: StratCue.ForAll("Blizzard III", "Keep walking round the ring through Exdeath's Blizzard III", YourGroup));
    }

    private (PartyRole First, PartyRole? Second) ThunderRoles => ThunderIIIPlanning.Roles(state.BridgeThunder);

    private IAiMove BusterTankHugsExdeathRestPastTheMiddle(bool secondHit)
    {
        var (first, second) = ThunderRoles;
        if (cast.Exdeath is not { } exdeath || secondHit && second is null) return ByRole(_ => null);
        var on = secondHit ? second!.Value : first;
        var boss = new Vector2(exdeath.Position.X, exdeath.Position.Z);
        var outward = boss.LengthSquared() > 1f ? Vector2.Normalize(boss) : new Vector2(0f, -1f);
        var hug = boss - outward * 1.5f;
        var past = boss - outward * ClearOfTheBuster;
        if (past.Length() > EndRingRadius + 6f) past = Vector2.Normalize(past) * (EndRingRadius + 6f);
        return ByRole(role => role == on ? hug : secondHit && role != first ? null : Spread(past, role));
    }

    private static Vector2 Spread(Vector2 at, PartyRole role)
    {
        var (sin, cos) = MathF.SinCos((int)role * MathF.PI / 4f);
        return at + new Vector2(sin, cos) * 0.8f;
    }

    private static IAiMove OwnEighthOfARingRoundTheMiddle(float turn) => ByRole(role =>
    {
        var (sin, cos) = MathF.SinCos((int)role * MathF.PI / 4f + turn);
        return new Vector2(sin, cos) * EndRingRadius;
    });

    private static IAiMove ByRole(Func<PartyRole, Vector2?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(at).ToArray()).NaturalOrder();
}
