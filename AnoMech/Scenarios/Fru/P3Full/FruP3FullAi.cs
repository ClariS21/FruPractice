using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P3Full;

public sealed class FruP3FullAi
{
    private static readonly PartyRole[] NonTanks = [.. Enum.GetValues<PartyRole>().Where(role => role is not (PartyRole.MainTank or PartyRole.OffTank))];

    // Shockwave Pulsar starts casting here; Black Halo hits at 81.8 + its 4.7 s cast + 0.3 s to the effect.
    private const float PulsarCastAt = 73.5f;
    private const float BlackHaloHitsAt = 86.8f;

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        // The party takes its Black Halo places once the Pulsar is on its way; nobody but the main tank is hit,
        // so the row is only where to stand; a room of 1 y keeps its 3 y spacing.
        ai.Move(PulsarCastAt, () => ByRole(BlackHaloSpot), knownAt: PulsarCastAt, knownBy: CueKind.Cast, room: 1f,
            jitter: 0.3f, arrivalTime: BlackHaloHitsAt);
    }

    private static Vector3? BlackHaloSpot(PartyRole role) => role switch
    {
        PartyRole.MainTank => new Vector3(0f, 0f, -6f),
        PartyRole.OffTank => null,
        _ => new Vector3((Array.IndexOf(NonTanks, role) - 2.5f) * 3f, 0f, 8f),
    };

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
