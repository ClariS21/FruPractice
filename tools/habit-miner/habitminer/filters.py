def dead_windows(record):
    result = {aid: [] for aid in record["players"]}
    for t, aid in record.get("deaths", []):
        if aid in result:
            for start, end in record["phases"].values():
                if start <= t <= end and [start, end] not in result[aid]:
                    result[aid].append([start, end])
                    break
    return result


def dirty_windows(record):
    result = {aid: [] for aid in record["players"]}
    fail = set(record.get("failDebuffs", []))
    for aid, applications in record.get("applied", {}).items():
        for t, ability, kind, _ in applications:
            if aid in result and kind == "debuff" and ability in fail:
                result[aid].append([t - 5, t + 2])
    for windows in result.values():
        windows.sort()
    return result


def keep(item, windows):
    return not item.get("forced", False) and all(item["t1"] < start or item["t0"] > end for start, end in windows)


def _percentile(sorted_values, p):
    index = (len(sorted_values) - 1) * p / 100
    lower = int(index)
    upper = min(lower + 1, len(sorted_values) - 1)
    return sorted_values[lower] + (sorted_values[upper] - sorted_values[lower]) * (index - lower)


def trim_outliers(values, lo=2, hi=98):
    if not values:
        return []
    ordered = sorted(values)
    low, high = _percentile(ordered, lo), _percentile(ordered, hi)
    return [value for value in values if low <= value <= high]
