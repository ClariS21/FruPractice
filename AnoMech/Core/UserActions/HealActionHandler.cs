using System.Linq;
using AnoMech.Core.Game.PartyHp;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AnoMech.Core.UserActions;

// A healer's own button heals the sim's party, through the path the bots' heals take: per member after its
// own server tick, with regens on the shared beat. Animation only otherwise; the client shows the player's
// own press itself.
internal sealed class HealActionHandler : IUserActionHandler
{
    public void OnAction(ActionType actionType, uint actionId, ulong targetId)
    {
        if (actionType != ActionType.Action || HealCatalogue.Find(actionId) is not { } heal) return;
        if (Plugin.Game is not { Player: { } player } game) return;
        var target = game.World.Party.AllMembers().FirstOrDefault(m => (ulong)m.GameObjectId == targetId);
        game.World.Healing.Perform(player, heal, target);
    }
}
