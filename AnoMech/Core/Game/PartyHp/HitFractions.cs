using System.Collections.Generic;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;

namespace AnoMech.Core.Game.PartyHp;

// Median share of max HP a hit takes, after mitigation, by role. P3 numbers are from our own
// pulls, P4 and P5 from public top clears. Keyed by the hit's action id, and by the cast's where
// the sim's EnemyAction uses that.
internal static class HitFractions
{
    private readonly record struct Row(float? Tank, float? Healer, float? Dps);

    private static readonly Dictionary<uint, Row> Table = Build();

    private static Dictionary<uint, Row> Build()
    {
        var t = new Dictionary<uint, Row>();
        void Add(Row row, params uint[] ids)
        {
            foreach (var id in ids) t[id] = row;
        }
        // P3
        Add(new(0.12f, 0.49f, 0.54f), 0x9D22);
        Add(new(0.22f, 0.55f, 0.54f), 0x9D4A);
        Add(new(0.16f, 0.39f, 0.50f), 0x9D54);
        Add(new(0.05f, 0.23f, 0.28f), 0x9D55);
        Add(new(0.16f, 0.55f, 0.47f), 0x9D2B);
        Add(new(0.16f, 0.28f, 0.37f), 0x9D52);
        Add(new(0.09f, 0.29f, 0.29f), 0x9D4F);
        Add(new(0.09f, 0.32f, 0.34f), 0x9D5E, 0x9D5F);
        Add(new(0.15f, 0.46f, 0.53f), 0x9D5A);
        Add(new(0.14f, 0.41f, 0.41f), 0x9D60, 0x9D61);
        Add(new(0.27f, null, null), 0x9D62);
        Add(new(0.30f, 0.77f, 0.81f), 0x9D68, 0x9D69);
        Add(new(0.02f, 0.08f, 0.08f), 0x9CF7);
        Add(new(0.79f, null, null), 0x9CF6);
        // P4
        Add(new(0.11f, 0.18f, 0.17f), 0x9CEE);
        Add(new(0.18f, 0.57f, 0.56f), 0x9CFE);
        Add(new(0.02f, 0.13f, 0.11f), 0x9D15);
        Add(new(0.19f, 0.48f, 0.45f), 0x9D2F);
        Add(new(0.19f, 0.62f, 0.65f), 0x9D3A, 0x9D70);
        Add(new(0.07f, 0.18f, 0.17f), 0x9D6F, 0x9D6E);
        Add(new(0.05f, 0.13f, 0.12f), 0x9D59);
        Add(new(1.00f, null, null), 0x9D5C);
        Add(new(0.48f, null, null), 0x9D5D);
        Add(new(0.15f, 0.15f, 0.16f), 0x9D8C);
        Add(new(0.06f, 0.15f, 0.13f), 0x9D58);
        Add(new(null, 0.6f, 0.6f), 0x9D57);
        // P5
        Add(new(0.20f, 0.59f, 0.50f), 0x9D72);
        Add(new(0.2f, 0.5f, 0.5f), 0x9D74, 0x9D75);
        Add(new(0.20f, 0.45f, 0.44f), 0x9D77, 0x9D78, 0x9D76);
        Add(new(0.12f, 0.36f, 0.35f), 0x9D7D, 0x9D7E);
        Add(new(0.12f, 0.29f, 0.28f), 0x9D80);
        Add(new(0.18f, 0.44f, 0.56f), 0x9D86);
        return t;
    }

    private const float DefaultTank = 0.15f;
    private const float DefaultHealer = 0.45f;
    private const float DefaultDps = 0.50f;
    private const float DefaultTankBuster = 0.45f;

    public static float For(uint actionId, Severity severity, PartyRole role)
    {
        if (Table.TryGetValue(actionId, out var row) && Pick(row, role) is { } known) return known;
        if (severity.Kind == SeverityKind.Harmless) return 0f;
        if (role.IsTank()) return severity.Kind == SeverityKind.TankBuster ? DefaultTankBuster : DefaultTank;
        return IsHealer(role) ? DefaultHealer : DefaultDps;
    }

    private static bool IsHealer(PartyRole role) => role is PartyRole.RegenHealer or PartyRole.ShieldHealer;

    private static float? Pick(Row row, PartyRole role)
        => role.IsTank() ? row.Tank : IsHealer(role) ? row.Healer : row.Dps;
}
