using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game.Ai;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Fru.P4Full;

public sealed class FruP4FullAi
{
    private static readonly Vector3 StackSpot = new(0f, 0f, 8f);

    public void Run(SimWorld world)
    {
        var ai = new AiManager(world);
        ai.Move(0.2f, () => ByRole(_ => Vector3.Zero), jitter: 0.3f, arrivalTime: 5f);
        ai.Move(75.5f, () => ByRole(_ => MornAfahSpot(world)), jitter: 0.3f, arrivalTime: 79.5f);
        var departures = world.Stream("p4-final-morn-afah-departure");
        foreach (var role in Enum.GetValues<PartyRole>())
        {
            var stackRole = role;
            ai.Move(157.8f + departures.NextSingle(), () => ByRole(r => r == stackRole ? FinalMornAfahSpot(r) : null),
                jitter: 0f, arrivalTime: 161.5f, settleFraction: 1f);
        }
    }

    private static Vector3 MornAfahSpot(SimWorld world)
        => world.Party.Get(PartyRole.MainTank) is { } tank && !world.Party.IsBotDriven(tank) ? tank.Position : StackSpot;

    private static Vector3 FinalMornAfahSpot(PartyRole role)
        => new(0f, 0f, ((int)role % 2 == 0 ? 1f : -1f) * (0.3f + (int)role / 2 * 0.15f));

    private static IAiMove ByRole(Func<PartyRole, Vector3?> at)
        => AiMove.Create(Enum.GetValues<PartyRole>().Select(role => at(role) is { } p ? new Vector2(p.X, p.Z) : (Vector2?)null).ToArray()).NaturalOrder();
}
