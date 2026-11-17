using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AnoMech.Scenarios;

namespace AnoMech.Core.Game.Ai.Habits;

public enum HabitRole { Tank, Healer, Melee, Ranged, Caster }
public enum HabitGroup { Speed, Decent }

// A small empirical distribution: sorted samples, read by linear interpolation. A rate or chance the
// data stores as one number is a single sample.
public sealed class Quantiles
{
    private readonly float[] values;

    public Quantiles(IEnumerable<float> values)
    {
        this.values = values.Order().ToArray();
        // Every metric is a time, distance, fraction, angle or rate: a negative one would turn a
        // settle or idle radius inside out.
        if (this.values.Length == 0 || this.values.Any(v => !float.IsFinite(v) || v < 0f))
            throw new ArgumentException("Quantiles must contain finite, nonnegative samples.", nameof(values));
    }

    public float Min => values[0];
    public float Max => values[^1];

    public float Sample(Rng rng) => At(rng.NextSingle());

    // The value at quantile position u in [0, 1].
    public float At(float u)
    {
        var index = Math.Clamp(u, 0f, 1f) * (values.Length - 1);
        var lo = (int)index;
        return values[lo] + (values[Math.Min(lo + 1, values.Length - 1)] - values[lo]) * (index - lo);
    }
}

public sealed record HabitMetrics
{
    public Quantiles? Debuff { get; init; }
    public Quantiles? Cast { get; init; }
    public Quantiles? Resolve { get; init; }
    public Quantiles? SlackUse { get; init; }
    public Quantiles? Settle { get; init; }
    public Quantiles? Bulge { get; init; }
    public Quantiles? PauseChance { get; init; }
    public Quantiles? Pause { get; init; }
    public Quantiles? OvershootChance { get; init; }
    public Quantiles? Overshoot { get; init; }
    public Quantiles? IdleRate { get; init; }
    public Quantiles? IdleStep { get; init; }
    public Quantiles? Angle { get; init; }
    public Quantiles? SwitchRate { get; init; }
    public Quantiles? CastStill { get; init; }
    // Keyed by boss name.
    public IReadOnlyDictionary<string, Quantiles> CentreDistance { get; init; } = new Dictionary<string, Quantiles>();

    public Quantiles Reaction(CueKind kind) => (kind switch
    {
        CueKind.Debuff => Debuff,
        CueKind.Resolve => Resolve,
        _ => Cast
    })!;

    internal HabitMetrics Fallback(HabitMetrics other) => new()
    {
        Debuff = Debuff ?? other.Debuff, Cast = Cast ?? other.Cast, Resolve = Resolve ?? other.Resolve,
        SlackUse = SlackUse ?? other.SlackUse, Settle = Settle ?? other.Settle,
        Bulge = Bulge ?? other.Bulge, PauseChance = PauseChance ?? other.PauseChance, Pause = Pause ?? other.Pause,
        OvershootChance = OvershootChance ?? other.OvershootChance, Overshoot = Overshoot ?? other.Overshoot,
        IdleRate = IdleRate ?? other.IdleRate, IdleStep = IdleStep ?? other.IdleStep,
        Angle = Angle ?? other.Angle, SwitchRate = SwitchRate ?? other.SwitchRate, CastStill = CastStill ?? other.CastStill,
        CentreDistance = other.CentreDistance.Concat(CentreDistance).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value)
    };
}

// One real player's anonymised movement habits. Metrics returned by MovementHabits.Draw have every
// fallback already applied.
public sealed record HabitProfile(string Id, HabitRole Role, HabitGroup Group, HabitMetrics Metrics);

// The shipped habit population (movement/habits.json, schema 1).
public sealed class MovementHabits
{
    // The bots of before the habit data: they leave after today's 0.15-1.15 s reaction with no slack
    // used, walk straight, stop dead and stand still.
    private static readonly HabitMetrics BuiltIn = new()
    {
        Debuff = TodaysReaction, Cast = TodaysReaction, Resolve = TodaysReaction,
        SlackUse = new([0f]), Settle = new([0f, 0.5f]), Bulge = new([0f]),
        PauseChance = new([0f]), Pause = new([0f]), OvershootChance = new([0f]), Overshoot = new([0f]),
        IdleRate = new([0f]), IdleStep = new([0f]), Angle = new([90f, 180f]), SwitchRate = new([0f]), CastStill = new([1f])
    };

    private static Quantiles TodaysReaction => new([0.223f, 0.295f, 0.35f, 0.396f, 0.437f, 0.474f, 0.508f, 0.54f, 0.57f, 0.6f,
        0.63f, 0.66f, 0.692f, 0.726f, 0.763f, 0.804f, 0.85f, 0.905f, 0.977f]);

    public static MovementHabits Default { get; } = new(
        Enum.GetValues<HabitRole>().Select(role => new HabitProfile($"default-{role}", role, HabitGroup.Speed, BuiltIn)).ToArray(),
        new Dictionary<HabitRole, HabitMetrics>(), new Dictionary<HabitGroup, float> { [HabitGroup.Speed] = 1f });

    public IReadOnlyList<HabitProfile> Profiles { get; }
    public IReadOnlyDictionary<HabitRole, HabitMetrics> RoleDefaults { get; }
    public IReadOnlyDictionary<HabitGroup, float> GroupWeights { get; }

    public MovementHabits(IReadOnlyList<HabitProfile> profiles, IReadOnlyDictionary<HabitRole, HabitMetrics> roleDefaults,
        IReadOnlyDictionary<HabitGroup, float> groupWeights)
    {
        Profiles = profiles;
        RoleDefaults = roleDefaults;
        GroupWeights = groupWeights;
    }

    // Where the build puts the data, beside the DLL.
    public static string PathIn(string directory) => Path.Combine(directory, "movement", "habits.json");

    // A null metric takes the role's pooled one, then the built-in one.
    public HabitMetrics Resolve(HabitProfile profile)
        => profile.Metrics.Fallback(RoleDefaults.GetValueOrDefault(profile.Role) ?? new()).Fallback(BuiltIn);

    // A group by GroupWeights, then one of its profiles of this role.
    public HabitProfile Draw(HabitRole role, Rng rng)
    {
        var groups = Enum.GetValues<HabitGroup>().Where(g => Profiles.Any(p => p.Role == role && p.Group == g)).ToArray();
        if (groups.Length == 0) return Default.Draw(role, rng);
        var roll = rng.NextSingle() * groups.Sum(g => GroupWeights.GetValueOrDefault(g));
        var group = groups[^1];
        foreach (var candidate in groups)
        {
            roll -= GroupWeights.GetValueOrDefault(candidate);
            if (roll < 0f) { group = candidate; break; }
        }
        var candidates = Profiles.Where(p => p.Role == role && p.Group == group).ToArray();
        var chosen = candidates[rng.Next(candidates.Length)];
        return chosen with { Metrics = Resolve(chosen) };
    }

    // Null when the file is missing, unreadable or not schema 1.
    public static MovementHabits? Load(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetInt32() != 1) return null;
            var profiles = root.GetProperty("profiles").EnumerateArray().Select(p => new HabitProfile(
                p.GetProperty("id").GetString()!, Parse<HabitRole>(p.GetProperty("role").GetString()!),
                Parse<HabitGroup>(p.GetProperty("group").GetString()!), ReadMetrics(p))).ToArray();
            var defaults = root.GetProperty("roleDefaults").EnumerateObject()
                .ToDictionary(p => Parse<HabitRole>(p.Name), p => ReadMetrics(p.Value));
            var weights = root.GetProperty("groupWeights").EnumerateObject()
                .ToDictionary(p => Parse<HabitGroup>(p.Name), p => p.Value.GetSingle());
            if (profiles.Length == 0 || profiles.Any(p => string.IsNullOrWhiteSpace(p.Id)) ||
                profiles.Select(p => p.Id).Distinct().Count() != profiles.Length ||
                weights.Values.Any(w => !float.IsFinite(w) || w < 0f) || weights.Values.Sum() <= 0f) return null;
            return new(profiles, defaults, weights);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or
                                     InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException or OverflowException)
        { return null; }
    }

    private static T Parse<T>(string text) where T : struct, Enum
        => Enum.TryParse<T>(text, true, out var value) && Enum.IsDefined(value) ? value : throw new FormatException(text);

    private static HabitMetrics ReadMetrics(JsonElement p)
    {
        Quantiles? Q(params string[] keys)
        {
            var value = p;
            foreach (var key in keys)
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value)) return null;
            return value.ValueKind == JsonValueKind.Null ? null : value.ValueKind == JsonValueKind.Array
                ? new Quantiles(value.EnumerateArray().Select(v => v.GetSingle())) : new Quantiles([value.GetSingle()]);
        }
        var distances = new Dictionary<string, Quantiles>();
        if (p.TryGetProperty("uptime", out var uptime) && uptime.ValueKind == JsonValueKind.Object &&
            uptime.TryGetProperty("centreDistance", out var centres) && centres.ValueKind == JsonValueKind.Object)
            foreach (var entry in centres.EnumerateObject())
                if (Q("uptime", "centreDistance", entry.Name) is { } q) distances.Add(entry.Name, q);
        return new()
        {
            Debuff = Q("reaction", "debuff"), Cast = Q("reaction", "cast"), Resolve = Q("reaction", "resolve"),
            SlackUse = Q("slackUse"), Settle = Q("settle"), Bulge = Q("path", "bulge"),
            PauseChance = Q("path", "pauseChance"), Pause = Q("path", "pause"),
            OvershootChance = Q("path", "overshootChance"), Overshoot = Q("path", "overshoot"),
            IdleRate = Q("idle", "rate"), IdleStep = Q("idle", "step"), Angle = Q("uptime", "angle"),
            SwitchRate = Q("uptime", "switchRate"), CastStill = Q("uptime", "castStill"), CentreDistance = distances
        };
    }
}
