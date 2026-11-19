using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game.Party;

// A scenario with more than one boss up at once says which one a seat's bot fights; null for the shared target.
// BotCombat.FindTarget(role) uses it only while that boss is targetable.
public interface IBotTargetPicker
{
    SimEnemy? TargetFor(PartyRole role);
}
