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

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(73f, () => ByRole(BlackHaloSpot), jitter: 0.3f, arrivalTime: 80.5f);
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
