using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Scenarios.Umad.P5Full;

// Kefka's highest enmity, which Surprise Flare, the tank Fell Forces and Chaotic Flare go to. He
// starts on the main tank; a tank takes him with Provoke, a bot through the strat and the player with
// their own press.
public sealed class UmadP5Enmity(SimWorld world, UmadP5Cast cast)
{
    public const uint Provoke = 7533;

    public PartyRole Holder { get; private set; } = PartyRole.MainTank;

    public PartyRole Second => Holder == PartyRole.MainTank ? PartyRole.OffTank : PartyRole.MainTank;

    public void Take(PartyRole tank)
    {
        Holder = tank;
        if (cast.Kefka is { } kefka && world.Party.Get(tank) is { } member) kefka.SetTarget(member, follow: false);
    }

    // The strat's press. The player presses their own, unless a bot drives their seat.
    public void ProvokeBy(PartyRole tank)
    {
        var member = world.Party.Get(tank);
        if (!member.IsAlive() || !world.Party.IsBotDriven(member!)) return;
        (member as SimPartyNpc)?.UseAction(Provoke, cast.Kefka);
        Take(tank);
    }
}
