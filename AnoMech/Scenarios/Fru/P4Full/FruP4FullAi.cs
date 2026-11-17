using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P4Full;

public sealed class FruP4FullAi
{
    private const float FirstMornAfahCastAt = 74.8f;
    private const float FirstMornAfahAt = 80.5f;
    private const float FinalMornAfahCastAt = 157.5f;
    private const float FinalMornAfahAt = 163.2f;

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.2f, () => ByRole(_ => Vector3.Zero), jitter: 0.3f, arrivalTime: 5f, stagingMove: true, knownAt: 0.2f, room: 2f);
        StackForMornAfah(ai, FirstMornAfahCastAt, FirstMornAfahAt);
        StackForMornAfah(ai, FinalMornAfahCastAt, FinalMornAfahAt);
    }

    private static void StackForMornAfah(AiManager ai, float castAt, float snapshotAt)
        => ai.Move(castAt, () => ByRole(MornAfahSpot), jitter: 0f, arrivalTime: snapshotAt, settleFraction: 1f, knownAt: castAt, room: 0.5f);

    private static Vector3? MornAfahSpot(PartyRole role)
        => new Vector3(0f, 0f, ((int)role % 2 == 0 ? 1f : -1f) * (0.3f + (int)role / 2 * 0.15f));

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
