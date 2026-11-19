using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using AnoMech.Core.Native.Interfaces;

namespace AnoMech.Core.ActBridge;

// An actor as ACT's log lines name it. Position is world space.
internal readonly record struct ActLineActor(
    uint Id, string Name, uint Hp = 0, uint MaxHp = 0, uint Mp = 0, uint MaxMp = 0, Vector3 Position = default, float Heading = 0f)
{
    public static readonly ActLineActor None = new(ActLine.NoActor, "");
}

// ACT network-log lines in the field order cactbot parses, without the timestamp and hash that
// ActBridge adds. Entity ids are 8 hex digits and action and status ids unpadded hex, as ACT writes them.
internal static class ActLine
{
    public const uint NoActor = 0xE0000000;
    private const int EffectPairs = 8;

    private static string Id(uint id) => (id == 0 ? NoActor : id).ToString("X8", CultureInfo.InvariantCulture);
    private static string Hex(uint value) => value.ToString("X", CultureInfo.InvariantCulture);
    private static string Hex(uint value, int digits) => value.ToString("X" + digits, CultureInfo.InvariantCulture);
    private static string F(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
    private static string F3(float value) => value.ToString("F3", CultureInfo.InvariantCulture);
    private static string Dec(uint value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Name(ActLineActor actor) => actor.Id is 0 or NoActor ? "" : actor.Name;

    public static string StartsUsing(ActLineActor source, uint actionId, string ability, ActLineActor target, float castSeconds)
        => $"20|{Id(source.Id)}|{source.Name}|{Hex(actionId)}|{ability}|{Id(target.Id)}|{Name(target)}|{F(castSeconds)}"
           + $"|{F(source.Position.X)}|{F(source.Position.Y)}|{F(source.Position.Z)}|{F(source.Heading)}";

    // One line per target of an ActionEffect. Type 22 when the packet had several targets.
    public static string ActionEffect(
        bool aoe, ActLineActor source, uint actionId, string ability, ActLineActor target,
        IReadOnlyList<ActionEffectEntry>? effects, uint sequence, int targetIndex, int targetCount,
        uint displayType, uint animationId, float animationLock, ushort rotation)
    {
        var pairs = new string[EffectPairs * 2];
        for (var i = 0; i < EffectPairs; i++)
        {
            var effect = effects is not null && i < effects.Count ? effects[i] : default;
            pairs[i * 2] = Hex((uint)(effect.Type | effect.Param0 << 8 | effect.Param1 << 16 | effect.Param2 << 24));
            pairs[i * 2 + 1] = effect.Type == 0 ? "0" : $"{effect.Value & 0xFF:X2}{effect.Value >> 8:X2}{effect.Param3:X2}{effect.Param4:X2}";
        }
        return $"{(aoe ? 22 : 21)}|{Id(source.Id)}|{source.Name}|{Hex(actionId)}|{ability}|{Id(target.Id)}|{Name(target)}|{string.Join('|', pairs)}"
               + $"|{Dec(target.Hp)}|{Dec(target.MaxHp)}|{Dec(target.Mp)}|{Dec(target.MaxMp)}||"
               + $"|{F(target.Position.X)}|{F(target.Position.Y)}|{F(target.Position.Z)}|{F(target.Heading)}"
               + $"|{Dec(source.Hp)}|{Dec(source.MaxHp)}|{Dec(source.Mp)}|{Dec(source.MaxMp)}||"
               + $"|{F(source.Position.X)}|{F(source.Position.Y)}|{F(source.Position.Z)}|{F(source.Heading)}"
               + $"|{Hex(sequence, 8)}|{targetIndex}|{targetCount}|||{Hex(displayType)}|{Hex(actionId)}|{Hex(animationId)}"
               + $"|{animationLock.ToString("F3", CultureInfo.InvariantCulture)}|{Hex(rotation, 4)}";
    }

    public static string CancelAbility(ActLineActor source, uint actionId, string ability, string reason)
        => $"23|{Id(source.Id)}|{source.Name}|{Hex(actionId)}|{ability}|{reason}";

    public static string WasDefeated(ActLineActor target, ActLineActor source)
        => $"25|{Id(target.Id)}|{target.Name}|{Id(source.Id)}|{Name(source)}";

    // The count field is the status's whole param, at least two hex digits: stacks for most, a VFX id
    // for some (Kefka's 808 carries 45F to 462).
    public static string GainsEffect(ushort statusId, string status, float duration, ActLineActor source, ActLineActor target, ushort param)
        => $"26|{Hex(statusId)}|{status}|{F(duration)}|{Id(source.Id)}|{Name(source)}|{Id(target.Id)}|{target.Name}"
           + $"|{Hex(param, 2)}|{Dec(target.MaxHp)}|{Dec(source.MaxHp)}";

    public static string LosesEffect(ushort statusId, string status, ActLineActor source, ActLineActor target, ushort param)
        => $"30|{Hex(statusId)}|{status}|0.00|{Id(source.Id)}|{Name(source)}|{Id(target.Id)}|{target.Name}"
           + $"|{Hex(param, 2)}|{Dec(target.MaxHp)}|{Dec(source.MaxHp)}";

    public static string ActorControl(uint directorId, uint command, uint data0 = 0)
        => $"33|{Id(directorId)}|{Hex(command, 8)}|{Hex(data0, 2)}|00|00|00";

    public static string GameLogEcho(string text)
        => $"00|0038||{text}|";

    public static string HeadMarker(ActLineActor target, uint markerId)
        => $"27|{Id(target.Id)}|{target.Name}|0000|0000|{Hex(markerId, 4)}|0000|0000|0000";

    public static string Tether(ActLineActor source, ActLineActor target, uint tetherId)
        => $"35|{Id(source.Id)}|{source.Name}|{Id(target.Id)}|{Name(target)}|0000|0000|{Hex(tetherId, 4)}|{Id(target.Id)}|000F|0000";

    public static string MapEffect(uint flags, byte location)
        => $"257|00000000|{Hex(flags, 8)}|{Hex(location, 2)}|0000|0000";

    public static string StartsUsingExtra(uint sourceId, uint actionId, Vector3 position, float heading)
        => $"263|{Id(sourceId)}|{Hex(actionId)}|{F(position.X)}|{F(position.Y)}|{F(position.Z)}|{F(heading)}";

    public static string ActorSetPos(uint id, Vector3 position, float heading)
        => $"271|{Id(id)}|{F(heading)}|00|00|{F(position.X)}|{F(position.Y)}|{F(position.Z)}";

    public static string ActorControlExtra(uint id, uint category, uint param1, uint param2, uint param3, uint param4)
        => $"273|{Id(id)}|{Hex(category, 4)}|{Hex(param1)}|{Hex(param2)}|{Hex(param3)}|{Hex(param4)}";

    // OverlayPlugin's line for every ActionEffect packet, after its 21/22 lines. Only the multi-target
    // packet carries a location (dataFlag 1); the heading is the packet's rotation either way.
    public static string AbilityExtra(uint sourceId, uint actionId, uint sequence, Vector3? location, float heading, uint animationTargetId)
        => $"264|{Id(sourceId)}|{Hex(actionId, 4)}|{Hex(sequence, 8)}|"
           + (location is { } at ? $"1|{F3(at.X)}|{F3(at.Y)}|{F3(at.Z)}" : "0|||")
           + $"|{F3(heading)}|{Id(animationTargetId)}";

    // OverlayPlugin's line for every NPC spawn packet: the tether and animation state the actor
    // spawns with, which then get no 35 or 273 of their own.
    public static string SpawnNpcExtra(uint id, uint parentId, ushort tetherId, byte animationState)
        => $"272|{Id(id)}|{Id(parentId)}|{Hex(tetherId, 4)}|{Hex(animationState, 2)}";
}
