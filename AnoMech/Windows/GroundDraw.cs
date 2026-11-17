using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace AnoMech.Windows;

public static class GroundDraw
{
    private static readonly uint ShadowColor = Rgba(0f, 0f, 0f, 0.8f);
    public const int CircleSegments = 48;
    public static void DrawGroundCircle(ImDrawListPtr draw, Vector3 center, float radius, uint color, float thickness)
    {
        var points = new List<Vector3>(CircleSegments + 1);
        for (var i = 0; i <= CircleSegments; i++)
        {
            var angle = MathF.Tau * i / CircleSegments;
            points.Add(center + new Vector3(MathF.Sin(angle), 0f, MathF.Cos(angle)) * radius);
        }
        DrawGroundPolyline(draw, points, color, thickness);
    }

    public static void DrawCross(ImDrawListPtr draw, Vector3 at, float size, uint color, float thickness)
    {
        DrawGroundLine(draw, at + new Vector3(-size, 0f, -size), at + new Vector3(size, 0f, size), color, thickness);
        DrawGroundLine(draw, at + new Vector3(-size, 0f, size), at + new Vector3(size, 0f, -size), color, thickness);
    }

    public static void DrawGroundLine(ImDrawListPtr draw, Vector3 a, Vector3 b, uint color, float thickness)
        => DrawGroundPolyline(draw, [a, b], color, thickness);

    // Long lines are split so a segment that runs behind the camera only loses its hidden part.
    private const float MaxSegmentLength = 1f;

    public static void DrawGroundPolyline(ImDrawListPtr draw, IReadOnlyList<Vector3> points, uint color, float thickness)
    {
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            var steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / MaxSegmentLength));
            for (var s = 0; s < steps; s++)
            {
                var p = Vector3.Lerp(a, b, (float)s / steps);
                var q = Vector3.Lerp(a, b, (float)(s + 1) / steps);
                if (Project(p) is not { } sp || Project(q) is not { } sq) continue;
                draw.AddLine(sp, sq, ShadowColor, thickness + 2f);
                draw.AddLine(sp, sq, color, thickness);
            }
        }
    }

    public static void Label(ImDrawListPtr draw, Vector3 at, string text, uint color)
    {
        if (Project(at + new Vector3(0f, 0.3f, 0f)) is not { } screen) return;
        var size = ImGui.CalcTextSize(text);
        var pos = screen - new Vector2(size.X / 2f, size.Y + 6f);
        draw.AddText(pos + new Vector2(1f, 1f), ShadowColor, text);
        draw.AddText(pos, color, text);
    }

    public static uint Rgba(float r, float g, float b, float a)
        => ((uint)(a * 255f) << 24) | ((uint)(b * 255f) << 16) | ((uint)(g * 255f) << 8) | (uint)(r * 255f);

    public static Vector2? Project(Vector3 world)
        => Plugin.GameGui.WorldToScreen(world, out var screen, out _) ? screen : null;
}
