using System.Numerics;
using AnoMech.Core.Game;

namespace AnoMech.Core.SimObjects;

// Position is scenario-space (relative to SimWorld.ScenarioOrigin). Use
// SimWorld.Coordinates.ToGlobal(...) if you need world coords for native interop.
// Rotation is absolute radians (independent of scenario origin).
public interface IPositioned
{
    Vector3 Position { get; }
    float Rotation { get; }

    // Where the server would judge this character at a snapshot. Only the local player differs from
    // Position: the server sees where its client was about a ping ago.
    Vector3 JudgedPosition => Position;
    float JudgedRotation => Rotation;
    static IPositioned From(Vector3 target) => new At(target);

    private readonly record struct At(Vector3 Position, float Rotation = 0f) : IPositioned;
}

public static class PositionedExtensions
{
    public static Placement Placement(this IPositioned p) => new(p.Position, p.Rotation);

    public static Placement JudgedPlacement(this IPositioned p) => new(p.JudgedPosition, p.JudgedRotation);
}
