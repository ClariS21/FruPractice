import math
import statistics


def _xy(point):
    return point[-2:]


def _centroid(points):
    return [statistics.mean(p[0] for p in points), statistics.mean(p[1] for p in points)]


def _trips(moves, rests):
    moves = sorted(moves, key=lambda m: m["t0"])
    if not moves:
        return []
    if "actorId" in moves[0]:
        rests = [rest for rest in rests if rest.get("actorId") == moves[0]["actorId"]]
    trips = [[moves[0]]]
    for move in moves[1:]:
        previous = trips[-1][-1]
        gap = [r for r in rests if previous["t1"] <= r["t0"] and r["t1"] <= move["t0"] and r["t1"] - r["t0"] <= .75]
        if gap and move["t0"] - previous["t1"] <= .75:
            trips[-1].append(move)
        else:
            trips.append([move])
    return trips


def profile_metrics(moves, rests, record, actor_id, role, role_reaction_median):
    result = {"reaction": {key: [] for key in ("debuff", "cast", "resolve")}, "slackUse": [],
              "path": {"bulge": [], "pauseChance": None, "pause": [], "overshootChance": None, "overshoot": []},
              "settle": [], "idle": {"rate": None, "step": []},
              "uptime": {"centreDistance": {}, "angle": [], "switchRate": None, "castStill": None}}
    for move in moves:
        cue, slack = move.get("cue"), move.get("slack")
        if cue and slack is not None and slack <= 1.5:
            result["reaction"][cue["type"]].append(move["t0"] - cue["t"])
    for move in moves:
        cue, slack = move.get("cue"), move.get("slack")
        if not cue or slack is None or slack < 3 or move.get("deadline") is None:
            continue
        samples = result["reaction"][cue["type"]]
        r = statistics.median(samples) if len(samples) >= 5 else role_reaction_median.get(cue["type"])
        if r is None:
            continue
        window = move["deadline"] - .8 - (move["t1"] - move["t0"]) - (cue["t"] + r)
        if window >= 1:
            result["slackUse"].append(max(0, min(1, (move["t0"] - cue["t"] - r) / window)))
    trips = _trips(moves, rests)
    pause_count = overshoot_count = 0
    for trip in trips:
        path = [point for move in trip for point in move["path"]]
        start, end = trip[0]["p0"], trip[-1]["p1"]
        length = math.dist(start, end)
        if length >= 2 and len(path) >= 4:
            perpendicular = max(abs((point[1] - start[1]) * (end[0] - start[0]) -
                                    (point[0] - start[0]) * (end[1] - start[1])) / length
                                for point in (_xy(p) for p in path))
            result["path"]["bulge"].append(perpendicular / length)
        inner = [r["t1"] - r["t0"] for a, b in zip(trip, trip[1:]) for r in rests
                 if a["t1"] <= r["t0"] <= r["t1"] <= b["t0"]]
        if inner:
            pause_count += 1
            result["path"]["pause"].extend(inner)
        if length > 0:
            cutoff = path[0][0] + .7 * (path[-1][0] - path[0][0])
            projection = max((((p[1] - end[0]) * (end[0] - start[0]) +
                               (p[2] - end[1]) * (end[1] - start[1])) / length
                              for p in path if p[0] >= cutoff), default=0)
            if projection > .3:
                overshoot_count += 1
                result["path"]["overshoot"].append(projection)
    if trips:
        result["path"]["pauseChance"] = pause_count / len(trips)
        result["path"]["overshootChance"] = overshoot_count / len(trips)
    # A rest without actorId belongs to the requested player; shared rests may carry actorId.
    own_rests = [r for r in rests if r.get("actorId", actor_id) == actor_id]
    for rest in own_rests:
        duration = rest["t1"] - rest["t0"]
        if duration >= 1 and rest.get("points"):
            own = _centroid([_xy(p) for p in rest["points"]])
            overlapping = [(actor_id, own)]
            for other in rests:
                aid = other.get("actorId")
                if aid is None or aid == actor_id or other["t1"] - other["t0"] < 1 or other["t0"] > rest["t1"] or other["t1"] < rest["t0"]:
                    continue
                overlapping.append((aid, _centroid([_xy(p) for p in other["points"]])))
            cluster = {actor_id}
            changed = True
            while changed:
                old = len(cluster)
                cluster.update(aid for aid, pos in overlapping if any(math.dist(pos, p) <= 2.5 for bid, p in overlapping if bid in cluster))
                changed = len(cluster) != old
            members = [pos for aid, pos in overlapping if aid in cluster]
            if len(cluster) >= 3:
                result["settle"].append(math.dist(own, _centroid(members)))
    total_idle = 0
    idle_steps = []
    for rest in own_rests:
        duration = rest["t1"] - rest["t0"]
        if duration < 2:
            continue
        total_idle += duration
        run = 0
        for a, b in zip(rest["points"], rest["points"][1:]):
            dt = b[0] - a[0]
            displacement = math.dist(_xy(a), _xy(b))
            if dt > 0 and displacement / dt > .4:
                run += displacement
            elif run:
                if run >= .2:
                    idle_steps.append(run)
                run = 0
        if run >= .2:
            idle_steps.append(run)
    if total_idle:
        result["idle"]["rate"] = len(idle_steps) / total_idle
        result["idle"]["step"] = idle_steps
    hits = []
    for hit in record.get("playerHits", {}).get(actor_id, []):
        t, boss, bx, by, facing, px, py = hit
        if facing is None or any(-4 <= cast[0] - t <= 2 for cast in record.get("enemyCasts", []) if cast[1] in ("begincast", "cast")):
            continue
        distance = math.dist((bx, by), (px, py))
        angle = abs(math.degrees(math.atan2(py - by, px - bx) - facing)) % 360
        angle = min(angle, 360 - angle)
        result["uptime"]["centreDistance"].setdefault(boss, []).append(distance)
        result["uptime"]["angle"].append(angle)
        hits.append((t, angle))
    if role == "melee" and len(hits) >= 2 and hits[-1][0] > hits[0][0]:
        bands = ["rear" if angle > 120 else "flank" if angle >= 60 else "front" for _, angle in hits]
        switches = sum(a != b and "front" not in (a, b) for a, b in zip(bands, bands[1:]))
        result["uptime"]["switchRate"] = switches * 60 / (hits[-1][0] - hits[0][0])
    if role in ("healer", "caster"):
        samples = record.get("samples", {}).get(actor_id, [])
        starts = {}
        still = []
        for t, typ, ability in record.get("playerCasts", {}).get(actor_id, []):
            if typ == "begincast":
                starts[ability] = t
            elif typ == "cast" and ability in starts:
                a, b = starts.pop(ability), t
                before = min(samples, key=lambda p: abs(p[0] - a), default=None)
                after = min(samples, key=lambda p: abs(p[0] - b), default=None)
                if before and after:
                    still.append(math.dist(before[1:3], after[1:3]) < .3)
        if still:
            result["uptime"]["castStill"] = sum(still) / len(still)
    return result
