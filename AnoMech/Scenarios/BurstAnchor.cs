using System.Collections.Generic;
using System.Linq;

namespace AnoMech.Scenarios;

// A boss cast that starts at `Time` on the scenario's clock, which the party's burst windows hang on.
public readonly record struct BurstAnchor(uint ActionId, float Time)
{
    public static IEnumerable<BurstAnchor> Shifted(IEnumerable<BurstAnchor> anchors, float by)
        => anchors.Select(a => a with { Time = a.Time + by });
}
