using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P5Full;

public sealed class FruP5FullAi
{
    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        // Open staging on the centre: the whole party, so 2 y of room.
        ai.Move(0.2f, () => ByRole(_ => Vector3.Zero), jitter: 0.3f, arrivalTime: 5f, stagingMove: true, knownAt: 0.2f, room: 2f);
        // Back to the centre for Pandora's Box (112.7): nothing is aimed, so the arrival is nominal.
        ai.Move(102f, () => ByRole(_ => Vector3.Zero), jitter: 0.3f, arrivalTime: 108f, stagingMove: true, knownAt: 102f, room: 2f);
        world.Events.Add(111f, () => UseTankLimitBreak(world));
    }

    private static void UseTankLimitBreak(SimWorld world)
    {
        if (!world.Party.UseLimitBreak(PartyRole.MainTank)) world.Party.UseLimitBreak(PartyRole.OffTank);
    }

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
