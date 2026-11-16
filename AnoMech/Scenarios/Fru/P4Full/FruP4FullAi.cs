using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P4Full;

public sealed class FruP4FullAi
{
    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.2f, () => ByRole(_ => Vector3.Zero), jitter: 0.3f, arrivalTime: 5f);
        // Both Morn Afahs are a full-party stack at the arena centre. The first departs after the last Akh Morn hit
        // and arrives before the 82.3 s damage; the final one departs at 157.8 s and arrives before the 165.0 s damage.
        StackAtCentre(ai, world, "p4-first-morn-afah-departure", 75.5f, 79.5f);
        StackAtCentre(ai, world, "p4-final-morn-afah-departure", 157.8f, 161.5f);
    }

    private static void StackAtCentre(AiManager ai, SimWorld world, string stream, float departAfter, float arriveBy)
    {
        var departures = world.Stream(stream);
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            var stackRole = role;
            ai.Move(departAfter + departures.NextSingle(), () => ByRole(r => r == stackRole ? MornAfahSpot(r) : null),
                jitter: 0f, arrivalTime: arriveBy, settleFraction: 1f);
        }
    }

    private static Vector3 MornAfahSpot(PartyRole role)
        => new(0f, 0f, ((int)role % 2 == 0 ? 1f : -1f) * (0.3f + (int)role / 2 * 0.15f));

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
