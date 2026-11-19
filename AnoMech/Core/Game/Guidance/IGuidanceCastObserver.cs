using System.Numerics;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Game.Guidance;

public interface IGuidanceCastObserver
{
    void CastStarted(EnemyAction action, SimCharacter caster, SimCharacter? target, Vector3? location, float firesAt);
    void CastResolved(EnemyAction action, SimCharacter caster, Placement origin);
}
