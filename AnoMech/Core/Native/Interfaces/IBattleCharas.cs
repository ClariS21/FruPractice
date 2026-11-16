using System.Collections.Generic;
using AnoMech.Core.Game;
using AnoMech.Core.Game.Party;
using AnoMech.Core.SimObjects;

namespace AnoMech.Core.Native.Interfaces;

// Creates and finds BattleCharas. Placements are world space.
public interface IBattleCharas
{
    IBattleCharaProxy LocalPlayer { get; }

    // The engine's own NpcSpawn handler builds the actor from a packet built from the config, a
    // few frames later; until then the proxy doesn't exist. Its
    // slot stays reserved until ReleaseSlot. Null when a sheet row is missing or no slot is free.
    IBattleCharaProxy? SpawnBattleNpcFromPacket(EnemySpawnConfig config, Placement placement, out uint entityId);

    // A party doppel built by the engine's own PlayerSpawn
    // handler; arrives and reserves its slot like SpawnBattleNpcFromPacket.
    IBattleCharaProxy? SpawnDoppel(PartyMemberPreset preset, BotLook? look, BotWeaponSet? weapons, byte level, uint maxHealth, Placement placement, out uint entityId);

    // The looks the main-tank bot can wear: shipped plus captured. Empty when there are none.
    IReadOnlyList<BotLook> LoadMainTankLooks();

    bool IsInCharacterManager(uint entityId);

    void ReleaseSlot(int slot);

}
