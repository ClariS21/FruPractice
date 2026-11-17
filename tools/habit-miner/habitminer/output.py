import hashlib
import json
import os
import statistics
from pathlib import Path


def quantiles(values: list[float]) -> list[float] | None:
    ordered = sorted(values)
    if len(ordered) < 5:
        return None
    if len(ordered) < 10:
        return [round(v, 3) for v in ordered]
    result = []
    for percent in range(5, 100, 5):
        index = (len(ordered) - 1) * percent / 100
        low = int(index)
        high = min(low + 1, len(ordered) - 1)
        result.append(round(ordered[low] + (ordered[high] - ordered[low]) * (index - low), 3))
    return result


def _shape(metrics):
    reaction = metrics.get("reaction", {})
    path = metrics.get("path", {})
    idle = metrics.get("idle", {})
    uptime = metrics.get("uptime", {})
    return {"reaction": {cue: quantiles(reaction.get(cue, [])) for cue in ("debuff", "cast", "resolve")},
            "slackUse": quantiles(metrics.get("slackUse", [])),
            "path": {"bulge": quantiles(path.get("bulge", [])), "pauseChance": path.get("pauseChance"),
                     "pause": quantiles(path.get("pause", [])), "overshootChance": path.get("overshootChance"),
                     "overshoot": quantiles(path.get("overshoot", []))},
            "settle": quantiles(metrics.get("settle", [])),
            "idle": {"rate": idle.get("rate"), "step": quantiles(idle.get("step", []))},
            "uptime": {"centreDistance": {boss: quantiles(values) for boss, values in sorted(uptime.get("centreDistance", {}).items())},
                       "angle": quantiles(uptime.get("angle", [])), "switchRate": uptime.get("switchRate"),
                       "castStill": uptime.get("castStill")}}


def _pooled(profiles):
    pooled = {"reaction": {cue: [] for cue in ("debuff", "cast", "resolve")}, "slackUse": [],
              "path": {key: [] for key in ("bulge", "pause", "overshoot")}, "settle": [],
              "idle": {"step": []}, "uptime": {"centreDistance": {}, "angle": []}}
    scalars = {"pauseChance": [], "overshootChance": [], "rate": [], "switchRate": [], "castStill": []}
    for profile in profiles:
        m = profile["metrics"]
        for cue in pooled["reaction"]:
            pooled["reaction"][cue].extend(m.get("reaction", {}).get(cue, []))
        pooled["slackUse"].extend(m.get("slackUse", []))
        pooled["settle"].extend(m.get("settle", []))
        for key in pooled["path"]:
            pooled["path"][key].extend(m.get("path", {}).get(key, []))
        pooled["idle"]["step"].extend(m.get("idle", {}).get("step", []))
        pooled["uptime"]["angle"].extend(m.get("uptime", {}).get("angle", []))
        for boss, values in m.get("uptime", {}).get("centreDistance", {}).items():
            pooled["uptime"]["centreDistance"].setdefault(boss, []).extend(values)
        for key in ("pauseChance", "overshootChance"):
            value = m.get("path", {}).get(key)
            if value is not None:
                scalars[key].append(value)
        for key, section in (("rate", "idle"), ("switchRate", "uptime"), ("castStill", "uptime")):
            value = m.get(section, {}).get(key)
            if value is not None:
                scalars[key].append(value)
    for key in ("pauseChance", "overshootChance"):
        pooled["path"][key] = round(statistics.median(scalars[key]), 3) if scalars[key] else None
    pooled["idle"]["rate"] = round(statistics.median(scalars["rate"]), 3) if scalars["rate"] else None
    for key in ("switchRate", "castStill"):
        pooled["uptime"][key] = round(statistics.median(scalars[key]), 3) if scalars[key] else None
    return pooled


def build_habits(profiles: list[dict], counts: dict, generated_at: str) -> dict:
    kept = [p for p in profiles if sum(len(v) for v in p["metrics"].get("reaction", {}).values()) >= 5
            and len(p["metrics"].get("settle", [])) >= 5]
    # The caller's drops dictionary carries the thin count into summary_markdown.
    counts.setdefault("drops", {})["thin"] = len(profiles) - len(kept)
    kept.sort(key=lambda p: (p["group"], p["role"], hashlib.sha1((str(p["key"]) + str(p["actor_id"])).encode()).hexdigest()))
    output = []
    for index, profile in enumerate(kept, 1):
        output.append({"id": f"p{index:04d}", "role": profile["role"], "group": profile["group"],
                       **_shape(profile["metrics"])})
    roles = sorted({p["role"] for p in kept})
    population = {group: {"clears": counts.get(group, {}).get("clears", 0),
                          "profiles": sum(p["group"] == group for p in kept)} for group in ("speed", "decent")}
    return {"schemaVersion": 1, "generatedAt": generated_at, "population": population,
            "groupWeights": {"speed": .667, "decent": .333},
            "roleDefaults": {role: _shape(_pooled([p for p in kept if p["role"] == role])) for role in roles},
            "profiles": output}


def write_json_atomic(obj, path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = Path(str(path) + ".tmp")
    temp.write_text(json.dumps(obj, indent=2, sort_keys=True, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(temp, path)


def _median(values):
    return f"{statistics.median(values):.3f}" if values else "no data"


def summary_markdown(habits: dict, drops: dict) -> str:
    lines = ["# Movement habits summary", ""]
    for group in ("speed", "decent"):
        lines.extend([f"## {group.capitalize()} clears", ""])
        for role in ("tank", "healer", "melee", "ranged", "caster"):
            profiles = [p for p in habits["profiles"] if p["group"] == group and p["role"] == role]
            if not profiles:
                lines.append(f"- {role}: 0 profiles.")
                continue
            reactions = ", ".join(f"{cue} {_median([v for p in profiles for v in (p['reaction'][cue] or [])])} s"
                                  for cue in ("debuff", "cast", "resolve"))
            slack = _median([v for p in profiles for v in (p["slackUse"] or [])])
            settle = _median([v for p in profiles for v in (p["settle"] or [])])
            idle = _median([p["idle"]["rate"] for p in profiles if p["idle"]["rate"] is not None])
            text = f"- {role}: {len(profiles)} profiles; reaction {reactions}; slack use {slack}; settle {settle} y; idle {idle} steps/s"
            if role == "melee":
                bosses = sorted({boss for p in profiles for boss in p["uptime"]["centreDistance"]})
                text += "; boss distance " + ", ".join(f"{boss} {_median([v for p in profiles for v in (p['uptime']['centreDistance'].get(boss) or [])])} y" for boss in bosses)
            lines.append(text + ".")
        lines.append("")
    lines.extend(["## Drops", ""])
    for reason in ("dead", "dirty", "forced", "thin", "missing-phase"):
        lines.append(f"- {reason}: {drops.get(reason, 0)}")
    return "\n".join(lines) + "\n"
