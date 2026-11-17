import argparse
import json
import os
import random
import statistics
import sys
from datetime import datetime, timezone
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from habitminer.client import Client
from habitminer.cues import attach
from habitminer.extract import fetch_clear
from habitminer.filters import dead_windows, dirty_windows, keep, trim_outliers
from habitminer.metrics import profile_metrics
from habitminer.output import build_habits, summary_markdown, write_json_atomic
from habitminer.select import fetch_all_rankings, fetch_decent_pool, pick_speed
from habitminer.tracks import clean, pieces, resample, segment


def build_parser():
    parser = argparse.ArgumentParser(description="Mine anonymised FRU movement habits from FFLogs clears")
    parser.add_argument("--creds", required=True, help="path to FFLogs client credentials")
    parser.add_argument("--speed", type=int, default=40, help="number of speed clears (default: 40)")
    parser.add_argument("--decent", type=int, default=20, help="number of death-free decent clears (default: 20)")
    parser.add_argument("--cache", default=".tmp/fflogs-cache", help="response cache directory")
    parser.add_argument("--out-json", default="AnoMech/Resources/movement/habits.json", help="public habits output")
    parser.add_argument("--out-summary", default="docs/movement-habits-summary.md", help="private Markdown summary")
    return parser


def _collect_clear(record, drops):
    death_windows = dead_windows(record)
    dirt_windows = dirty_windows(record)
    players = {}
    all_rests = []
    for aid, info in record["players"].items():
        moves, rests = [], []
        for piece in pieces(clean(record["samples"].get(aid, []))):
            piece_moves, piece_rests = segment(resample(piece))
            moves.extend(piece_moves)
            rests.extend(piece_rests)
        windows_dead = death_windows[aid]
        windows_dirty = dirt_windows[aid]
        accepted_moves = []
        for move in moves:
            if move["forced"]:
                drops["forced"] += 1
            elif not keep(move, windows_dead):
                drops["dead"] += 1
            elif not keep(move, windows_dirty):
                drops["dirty"] += 1
            else:
                move = attach(move, record["applied"].get(aid, []), record["enemyCasts"], record["damageTaken"])
                move["actorId"] = aid
                accepted_moves.append(move)
        accepted_rests = []
        for rest in rests:
            if not keep(rest, windows_dead):
                drops["dead"] += 1
            elif not keep(rest, windows_dirty):
                drops["dirty"] += 1
            else:
                accepted_rests.append({**rest, "actorId": aid})
        all_rests.extend(accepted_rests)
        players[aid] = {"role": info["role"], "moves": accepted_moves}
    return players, all_rests


def _numeric_leaves(value, path=()):
    if isinstance(value, dict):
        for key, child in value.items():
            yield from _numeric_leaves(child, path + (key,))
    elif isinstance(value, list):
        for item in value:
            if isinstance(item, (int, float)) and not isinstance(item, bool):
                yield path, item
    elif isinstance(value, (int, float)) and not isinstance(value, bool):
        yield path, value


def _trim_profile(value, limits, path=()):
    if isinstance(value, dict):
        return {key: _trim_profile(child, limits, path + (key,)) for key, child in value.items()}
    if path not in limits:
        return value
    low, high = limits[path]
    if isinstance(value, list):
        return [item for item in value if low <= item <= high]
    return value if value is None or low <= value <= high else None


def _trim_by_role(profiles):
    pooled = {}
    for profile in profiles:
        for path, value in _numeric_leaves(profile["metrics"]):
            pooled.setdefault((profile["role"], path), []).append(value)
    limits = {}
    for (role, path), values in pooled.items():
        trimmed = trim_outliers(values)
        if trimmed:
            limits.setdefault(role, {})[path] = (min(trimmed), max(trimmed))
    for profile in profiles:
        profile["metrics"] = _trim_profile(profile["metrics"], limits.get(profile["role"], {}))


def _generated_at(path):
    try:
        existing = json.loads(Path(path).read_text(encoding="utf-8"))
        if existing.get("schemaVersion") == 1 and existing.get("generatedAt"):
            return existing["generatedAt"]
    except (OSError, ValueError):
        pass
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _write_summary(text, path):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = Path(str(path) + ".tmp")
    temp.write_text(text, encoding="utf-8")
    os.replace(temp, path)


def main(argv=None):
    args = build_parser().parse_args(argv)
    if args.speed < 0 or args.decent < 0:
        raise ValueError("Clear counts must be non-negative")
    client = Client(args.creds, args.cache)
    ranked = fetch_all_rankings(client)
    drops = {reason: 0 for reason in ("dead", "dirty", "forced", "thin", "missing-phase")}
    clears = []
    speed_pairs = pick_speed(ranked, args.speed)
    for code, fight in speed_pairs:
        record = fetch_clear(client, code, fight, "speed")
        if record is None:
            drops["missing-phase"] += 1
        else:
            clears.append(record)
    decent_count = 0
    decent_pool = fetch_decent_pool(client, random.Random(1079), exclude=set(speed_pairs)) if args.decent else []
    print(f"decent pool: {len(decent_pool)} candidates")
    for code, fight in decent_pool:
        if decent_count >= args.decent:
            break
        record = fetch_clear(client, code, fight, "decent")
        if record is None:
            drops["missing-phase"] += 1
        elif any(dead_windows(record).values()):
            drops["dead"] += 1
        else:
            clears.append(record)
            decent_count += 1
    processed = [(record, *_collect_clear(record, drops)) for record in clears]
    short_reactions = {}
    for _, players, _ in processed:
        for player in players.values():
            for move in player["moves"]:
                if move["cue"] and move["slack"] is not None and move["slack"] <= 1.5:
                    short_reactions.setdefault((player["role"], move["cue"]["type"]), []).append(move["t0"] - move["cue"]["t"])
    profiles = []
    for record, players, all_rests in processed:
        for aid, player in players.items():
            role_medians = {cue: statistics.median(values) for (role, cue), values in short_reactions.items()
                            if role == player["role"]}
            metrics = profile_metrics(player["moves"], all_rests, record, aid, player["role"], role_medians)
            profiles.append({"key": record["key"], "actor_id": aid, "group": record["group"],
                             "role": player["role"], "metrics": metrics})
    _trim_by_role(profiles)
    counts = {group: {"clears": sum(record["group"] == group for record in clears)} for group in ("speed", "decent")}
    counts["drops"] = drops
    habits = build_habits(profiles, counts, _generated_at(args.out_json))
    write_json_atomic(habits, args.out_json)
    _write_summary(summary_markdown(habits, drops), args.out_summary)
    print(f"clears: {len(clears)}")
    for group in ("speed", "decent"):
        for role in ("tank", "healer", "melee", "ranged", "caster"):
            count = sum(p["group"] == group and p["role"] == role for p in habits["profiles"])
            print(f"{group} {role} profiles: {count}")
    for reason, count in drops.items():
        print(f"{reason}: {count}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
