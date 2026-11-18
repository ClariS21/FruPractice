using System;
using System.Linq;
using System.Numerics;
using AnoMech.Core.Game;
using static AnoMech.Scenarios.Umad.UmadConstants;

namespace AnoMech.Scenarios.Umad;

// Implosion's two Shockwave sets, from where Chaos stood and faced as the cast began: two cones of
// ±45° along one axis, then two along the perpendicular. Longitudinal fires along its facing first,
// Latitudinal across it. Only the four diagonals between the axes are clear of both sets.
internal static class UmadImplosion
{
    public static float[] ConeAxes(float facing, uint attack, int set)
    {
        var axis = facing + (attack == ActionId.LongitudinalImplosion ? 0f : MathF.PI / 2f) + set * MathF.PI / 2f;
        return [axis, axis + MathF.PI];
    }

    public static int NearestDiagonal(Placement chaos, uint attack, Vector2 toward)
    {
        var bearing = MathF.Atan2(toward.X - chaos.Position.X, toward.Y - chaos.Position.Z);
        return Enumerable.Range(0, 4).MinBy(k => MathF.Abs(Wrap(Diagonal(chaos, attack, k) - bearing)));
    }

    // On `diagonal`, `distance` from Chaos, turned `lean` off it away from set `set`'s cones.
    public static Vector2 SafeSpot(Placement chaos, uint attack, int set, int diagonal, float distance, float lean)
    {
        var away = (diagonal + set) % 2 == 0 ? 1f : -1f;
        var (sin, cos) = MathF.SinCos(Diagonal(chaos, attack, diagonal) + away * lean);
        return new Vector2(chaos.Position.X + sin * distance, chaos.Position.Z + cos * distance);
    }

    private static float Diagonal(Placement chaos, uint attack, int diagonal)
        => ConeAxes(chaos.Rotation, attack, 0)[0] + MathF.PI / 4f + diagonal * MathF.PI / 2f;

    private static float Wrap(float angle) => MathF.IEEERemainder(angle, MathF.Tau);
}
