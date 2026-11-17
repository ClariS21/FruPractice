using System;
using System.Numerics;

namespace AnoMech.Scenarios.Fru;

// How far ahead the hazards that follow a player (or a player's aim) are reported, and how coarsely: they are
// only known once the player is where the mechanic will find them, and a finer step would hand the guidance run
// a new hazard on every frame.
public static class HazardReveal
{
    public const float Seconds = 1.5f;
    public const float Step = 0.25f;

    public static bool Near(float at, float now) => at - now <= Seconds;

    public static Vector2 Snap(Vector3 at) => new(MathF.Round(at.X / Step) * Step, MathF.Round(at.Z / Step) * Step);
}
