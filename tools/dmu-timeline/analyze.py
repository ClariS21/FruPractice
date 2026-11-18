"""Per-fight extraction + cross-fight aggregation for DMU P3-P5. Anonymised output only."""
import sys, json, random, statistics, itertools, collections
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import fetch

ROOT = HERE.parent.parent
OUT = ROOT / "docs" / "dmu" / "timelines"
PHASES = {3: "P3: Exdeath and Chaos", 4: "P4: Kefka Says", 5: "P5: Ultima Kefka"}
CLUSTER_WIN = 0.5
HF_COUNT = 20
SKIP_NAMES = {"Attack", "attack", "Shot"}


def med(v):
    v = [x for x in v if x is not None]
    return statistics.median(v) if v else None


def r3(x):
    return None if x is None else round(x, 3)


def cluster(items, tkey, win=CLUSTER_WIN):
    items = sorted(items, key=tkey)
    out, cur, start = [], [], None
    for it in items:
        t = tkey(it)
        if cur and t - start > win:
            out.append(cur)
            cur = []
        if not cur:
            start = t
        cur.append(it)
    if cur:
        out.append(cur)
    return out


def select_fights(n_top=15, seed=1085):
    ranked = fetch.rankings(12)
    pool = [r for r in ranked if r["deaths"] == 0 and r["size"] in (8, None)]
    top = pool[:n_top]
    rest = [r for r in pool[n_top:] if r["rank"] > 100]
    random.Random(seed).shuffle(rest)
    return [dict(r, group="top") for r in top], [dict(r, group="deep") for r in rest]


def analyse_fight(data):
    """Return {phase: {...}} or None if a player died."""
    fight = data["fight"]
    actors = {a["id"]: a for a in data["actors"]}
    names = {a["gameID"]: a["name"] for a in data["abilities"]}
    players = [a for a in actors.values() if a["type"] == "Player" and a["id"] in fight["friendlyPlayers"]]
    count = collections.Counter(p["subType"] for p in players)
    seen = collections.Counter()
    label = {}
    for p in sorted(players, key=lambda a: a["id"]):
        j = p["subType"]
        seen[j] += 1
        label[p["id"]] = j if count[j] == 1 else f"{j}#{seen[j]}"

    def is_player(i):
        return i in label

    def is_enemy(i):
        a = actors.get(i)
        return a is not None and a["type"] not in ("Player", "Pet") and not a.get("petOwner")

    def npc(i):
        a = actors[i]
        return (a["name"], a.get("gameID"))

    def aname(aid):
        return names.get(aid) or (f"status {aid - 1000000}" if aid >= 1000000 else f"ability {aid}")

    def who(i):
        return label.get(i) or (npc(i)[0] if i in actors else "?")

    tr = {t["id"]: t["startTime"] for t in fight["phaseTransitions"]}
    bounds = {3: (tr[3], tr[4]), 4: (tr[4], tr[5]), 5: (tr[5], fight["endTime"])}
    evs = data["events"]
    if any(e["type"] == "death" and is_player(e.get("targetID")) for e in evs):
        return None
    result = {}
    for ph, (s, e) in bounds.items():
        def sec(ts, s=s):
            return (ts - s) / 1000.0
        pe = [x for x in evs if s <= x["timestamp"] < e]
        units = collections.defaultdict(list)
        tsec = lambda x: x["timestamp"] / 1000.0

        # ---- casts
        begins = collections.defaultdict(list)
        casts = collections.defaultdict(list)
        for x in pe:
            if x["type"] in ("begincast", "cast") and is_enemy(x.get("sourceID")):
                aid = x["abilityGameID"]
                if aname(aid) in SKIP_NAMES:
                    continue
                (begins if x["type"] == "begincast" else casts)[aid].append(x)
        dmg_by = collections.defaultdict(list)
        for x in pe:
            if x["type"] == "damage" and is_enemy(x.get("sourceID")) and is_player(x.get("targetID")) \
                    and aname(x["abilityGameID"]) not in SKIP_NAMES:
                dmg_by[x["abilityGameID"]].append(x)
        dmg_clusters = {aid: cluster(v, tsec, 0.7) for aid, v in dmg_by.items()}
        for aid in set(begins) | set(casts):
            bc = cluster(begins.get(aid, []), tsec)
            cc = cluster(casts.get(aid, []), tsec)
            used = set()
            cl = []
            for b in bc:
                bt = tsec(b[0])
                dur = med([x.get("duration", 0) / 1000.0 for x in b])
                match = None
                for ci, c in enumerate(cc):
                    if ci not in used and bt - 0.05 <= tsec(c[0]) <= bt + dur + 3.0:
                        match = ci
                        break
                if match is not None:
                    used.add(match)
                cl.append({"b": b, "c": cc[match] if match is not None else None})
            for ci, c in enumerate(cc):
                if ci not in used:
                    cl.append({"b": None, "c": c})
            for u in cl:
                u["t0"] = u["b"][0]["timestamp"] if u["b"] else u["c"][0]["timestamp"]
            cl.sort(key=lambda u: u["t0"])
            for i, u in enumerate(cl):
                begin = sec(u["b"][0]["timestamp"]) if u["b"] else None
                resolve = sec(u["c"][0]["timestamp"]) if u["c"] else None
                ref = resolve if resolve is not None else begin
                nxt = sec(cl[i + 1]["t0"]) if i + 1 < len(cl) else 1e9
                dmg = None
                for dcl in dmg_clusters.get(aid, []):
                    dt = sec(dcl[0]["timestamp"])
                    if ref - 0.3 <= dt < nxt:
                        dmg = dt
                        break
                allev = (u["b"] or []) + (u["c"] or [])
                tg = sorted({label[x["targetID"]] for x in allev if is_player(x.get("targetID"))})
                srcs = collections.Counter(npc(x["sourceID"]) for x in allev)
                units[("cast", aid)].append({
                    "t": begin if begin is not None else resolve,
                    "begin": begin, "resolve": resolve, "damage": dmg,
                    "cast_time": med([x.get("duration", 0) / 1000.0 for x in u["b"]]) if u["b"] else None,
                    "targets": tg, "n_src": len({x["sourceID"] for x in allev}),
                    "source": list(srcs.most_common(1)[0][0])})

        # ---- damage clusters
        for aid, cls in dmg_clusters.items():
            for dcl in cls:
                un = [x.get("unmitigatedAmount", x.get("amount", 0)) for x in dcl]
                units[("damage", aid)].append({
                    "t": sec(dcl[0]["timestamp"]), "hits": len(dcl),
                    "n_targets": len({x["targetID"] for x in dcl}), "unmit": un,
                    "lethal": sum(1 for x in dcl if x.get("overkill", 0) > 0),
                    "targets": sorted({label[x["targetID"]] for x in dcl}),
                    "source": list(collections.Counter(npc(x["sourceID"]) for x in dcl).most_common(1)[0][0])})

        # ---- statuses enemies apply to players
        applies = collections.defaultdict(list)
        removes = collections.defaultdict(list)
        stacks = collections.defaultdict(list)
        for x in pe:
            if not (is_player(x.get("targetID")) and is_enemy(x.get("sourceID"))):
                continue
            tp = x["type"]
            if tp in ("applydebuff", "applybuff"):
                applies[x["abilityGameID"]].append(x)
            elif tp in ("removedebuff", "removebuff"):
                removes[(x["abilityGameID"], x["targetID"])].append(x["timestamp"])
            elif tp == "applybuffstack":
                stacks[(x["abilityGameID"], x["targetID"])].append((x["timestamp"], x.get("stack", 1)))
        for aid, v in applies.items():
            for cl in cluster(v, tsec):
                rem, dobs, stk, dfield = [], [], [], []
                for x in cl:
                    rts = [r for r in removes.get((aid, x["targetID"]), []) if r >= x["timestamp"]]
                    if rts:
                        rem.append(sec(rts[0]))
                        dobs.append((rts[0] - x["timestamp"]) / 1000.0)
                    sk = [st for ts, st in stacks.get((aid, x["targetID"]), [])
                          if x["timestamp"] <= ts < (rts[0] if rts else 1e18)]
                    stk.append(max(sk + [x.get("stack", 1)]))
                    if x.get("duration"):
                        dfield.append(x["duration"] / 1000.0)
                units[("status", aid)].append({
                    "t": sec(cl[0]["timestamp"]), "remove": min(rem) if rem else None,
                    "duration_obs": med(dobs), "duration_field": med(dfield),
                    "n": len(cl), "recipients": sorted({label[x["targetID"]] for x in cl}),
                    "stacks": max(stk) if stk else None,
                    "type": "debuff" if cl[0]["type"] == "applydebuff" else "buff",
                    "source": list(collections.Counter(npc(x["sourceID"]) for x in cl).most_common(1)[0][0])})

        # ---- headmarkers
        hm = collections.defaultdict(list)
        for x in pe:
            if x["type"] == "headmarker":
                rec = x["sourceID"] if is_player(x.get("sourceID")) else x.get("targetID")
                hm[x["markerID"]].append((x, rec))
        for mid, v in hm.items():
            for cl in cluster(v, lambda p: tsec(p[0])):
                units[("headmarker", mid)].append({
                    "t": sec(cl[0][0]["timestamp"]), "n": len(cl),
                    "recipients": sorted(who(r) for _, r in cl)})

        # ---- tethers
        th = collections.defaultdict(list)
        for x in pe:
            if x["type"] == "tether":
                th[x.get("tetherID")].append(x)
        for tid, v in th.items():
            for cl in cluster(v, tsec):
                units[("tether", tid)].append({
                    "t": sec(cl[0]["timestamp"]), "n": len(cl),
                    "ends": sorted({f"{who(x['sourceID'])}->{who(x['targetID'])}" for x in cl})})

        # ---- targetability and enemy deaths
        tg = collections.defaultdict(list)
        for x in pe:
            if x["type"] == "targetabilityupdate" and x.get("sourceID") in actors:
                tg[(npc(x["sourceID"]), int(x.get("targetable", 0)))].append(x)
            elif x["type"] == "death" and is_enemy(x.get("targetID")):
                tg[(npc(x["targetID"]), "death")].append(x)
        for (nm, state), v in tg.items():
            for cl in cluster(v, tsec):
                units[("targetable", f"{nm[0]}|{nm[1]}|{state}")].append({"t": sec(cl[0]["timestamp"]), "n": len(cl)})

        # ---- npc first seen
        first = {}
        for x in pe:
            sid = x.get("sourceID")
            if is_enemy(sid):
                first.setdefault(sid, x["timestamp"])
        byn = collections.defaultdict(list)
        for sid, ts in first.items():
            byn[npc(sid)].append(ts)
        for nm, v in byn.items():
            units[("npc_seen", f"{nm[0]}|{nm[1]}")].append({"t": sec(min(v)), "n": len(v)})

        nm_map = {k[1]: aname(k[1]) for k in units if k[0] in ("cast", "damage", "status")}
        result[ph] = {"duration": (e - s) / 1000.0, "units": units, "names": nm_map}
    return result


def stat(vals):
    v = [x for x in vals if x is not None]
    if not v:
        return None
    return {"median": r3(statistics.median(v)), "min": r3(min(v)), "max": r3(max(v)), "n": len(v),
            "raw": [r3(x) for x in vals]}


def aggregate(per_fight, ph, N):
    keys = set()
    for fr in per_fight:
        keys |= set(fr[ph]["units"].keys())
    names = {}
    for fr in per_fight:
        names.update(fr[ph]["names"])
    events = []
    for key in keys:
        kind, kid = key
        lists = [fr[ph]["units"].get(key, []) for fr in per_fight]
        counts = [len(l) for l in lists]
        hf = kind in ("status", "damage") and statistics.median(counts) > HF_COUNT
        if hf:
            ev = {"kind": kind, "id": kid, "name": names.get(kid), "high_frequency": True,
                  "count": stat([float(c) for c in counts]),
                  "first_t": stat([l[0]["t"] if l else None for l in lists]),
                  "last_t": stat([l[-1]["t"] if l else None for l in lists])}
            if kind == "damage":
                allv = [v for l in lists for u in l for v in u["unmit"]]
                ev["unmit"] = {"min": min(allv), "median": med(allv), "max": max(allv)} if allv else None
            ev["median_t"] = ev["first_t"]["median"] if ev["first_t"] else 0
            ev["fights_present"] = sum(1 for c in counts if c)
            ev["flags"] = ["high_frequency (tick-like/repeated), summarized"]
            events.append(ev)
            continue
        for k in range(max(counts)):
            us = [l[k] if k < len(l) else None for l in lists]
            present = [u for u in us if u]
            ev = {"kind": kind, "id": kid, "k": k + 1,
                  "name": names.get(kid) if kind in ("cast", "damage", "status") else None,
                  "fights_present": len(present)}
            flags = []

            def top_source():
                c = collections.Counter(tuple(u["source"]) for u in present).most_common(1)[0][0]
                return {"name": c[0], "gameId": c[1]}
            if kind == "cast":
                ev["source"] = top_source()
                ev["begincast"] = stat([u["begin"] if u else None for u in us])
                ev["cast_time_s"] = stat([u["cast_time"] if u else None for u in us])
                ev["resolve"] = stat([u["resolve"] if u else None for u in us])
                ev["damage"] = stat([u["damage"] if u else None for u in us])
                ev["targets"] = [u["targets"] if u else None for u in us]
                ev["n_sources_median"] = med([u["n_src"] for u in present])
                tf = ["begincast", "resolve", "damage"]
            elif kind == "damage":
                ev["source"] = top_source()
                ev["time"] = stat([u["t"] if u else None for u in us])
                allv = [v for u in present for v in u["unmit"]]
                ev["hits_per_cluster_median"] = med([u["hits"] for u in present])
                ev["targets_hit_median"] = med([u["n_targets"] for u in present])
                ev["unmit"] = {"min": min(allv), "median": med(allv), "max": max(allv)} if allv else None
                ev["lethal_hits"] = sum(u["lethal"] for u in present)
                ev["targets"] = [u["targets"] if u else None for u in us]
                tf = ["time"]
            elif kind == "status":
                ev["status_type"] = present[0]["type"]
                ev["source"] = top_source()
                ev["apply"] = stat([u["t"] if u else None for u in us])
                ev["remove"] = stat([u["remove"] if u else None for u in us])
                ev["duration_observed_s"] = stat([u["duration_obs"] if u else None for u in us])
                ev["duration_field_s"] = med([u["duration_field"] for u in present if u["duration_field"]])
                ev["recipients_n_median"] = med([u["n"] for u in present])
                ev["recipients"] = [u["recipients"] if u else None for u in us]
                ev["max_stacks"] = max([u["stacks"] or 1 for u in present])
                tf = ["apply", "remove"]
            elif kind == "headmarker":
                ev["time"] = stat([u["t"] if u else None for u in us])
                ev["recipients"] = [u["recipients"] if u else None for u in us]
                tf = ["time"]
            elif kind == "tether":
                ev["time"] = stat([u["t"] if u else None for u in us])
                ev["ends"] = [u["ends"] if u else None for u in us]
                tf = ["time"]
            else:
                ev["time"] = stat([u["t"] if u else None for u in us])
                ev["count_median"] = med([u["n"] for u in present])
                tf = ["time"]
            for fld in tf:
                s_ = ev.get(fld)
                if s_ and s_["max"] - s_["min"] > 2.0:
                    flags.append(f"{fld} varies {s_['max'] - s_['min']:.1f}s (min {s_['min']}, max {s_['max']})")
            if len(present) < N:
                flags.append(f"present in {len(present)}/{N} fights")
            ev["flags"] = flags
            ev["median_t"] = r3(statistics.median([u["t"] for u in present]))
            ev["_t"] = [u["t"] if u else None for u in us]
            events.append(ev)
    events.sort(key=lambda e: (e["median_t"], e["kind"], str(e["id"])))
    return events


def label_of(e):
    base = f"{e['kind']}:{e.get('name') or e['id']}[{e['id']}]"
    return f"{base}#{e['k']}" if e.get("k") else base


def order_groups(events, N):
    cand = [e for e in events if e["kind"] in ("cast", "headmarker", "status") and not e.get("high_frequency")
            and e["fights_present"] >= max(3, int(0.5 * N)) and e.get("name") not in SKIP_NAMES]
    parent = list(range(len(cand)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a
    pairs = []
    for i, a in enumerate(cand):
        for j in range(i + 1, len(cand)):
            b = cand[j]
            if b["median_t"] - a["median_t"] > 6.0:
                break
            both = [(x, y) for x, y in zip(a["_t"], b["_t"]) if x is not None and y is not None and abs(x - y) > 0.15]
            if len(both) < 3:
                continue
            frac = sum(1 for x, y in both if x < y) / len(both)
            if min(frac, 1 - frac) >= 0.1:
                pairs.append((i, j))
                parent[find(i)] = find(j)
    groups = collections.defaultdict(set)
    for i, j in pairs:
        groups[find(i)].update((i, j))
    out = []
    for mem in groups.values():
        evs = [cand[m] for m in sorted(mem)]
        perms = collections.Counter()
        for f in range(N):
            if all(e["_t"][f] is not None for e in evs):
                perms[tuple(label_of(e) for e in sorted(evs, key=lambda e: e["_t"][f]))] += 1
        out.append({"members": [label_of(e) for e in evs], "median_times": [e["median_t"] for e in evs],
                    "orders_seen": [{"order": list(o), "fights": c} for o, c in perms.most_common()]})
    out.sort(key=lambda g: min(g["median_times"]))
    return out


def main():
    top, deep = select_fights()
    rejected = collections.Counter()
    per_fight, meta = [], []

    def try_add(r):
        d = fetch.fetch_fight(r["code"], r["fight"])
        if d is None or "skip" in d:
            rejected["missing phase/kill"] += 1
            return False
        res = analyse_fight(d)
        if res is None:
            rejected["player death in P3-P5"] += 1
            return False
        per_fight.append(res)
        meta.append({"group": r["group"], "rank": r["rank"], "start": d["fight"]["startTime"]})
        return True

    top_ok = sum(try_add(r) for r in top)
    deep_ok = 0
    for r in deep:
        if deep_ok >= 12:
            break
        deep_ok += try_add(r)
    print("top usable", top_ok, "/", len(top), "deep usable", deep_ok, "rejected", dict(rejected))
    N = len(per_fight)
    for i, m in enumerate(meta):
        m["id"] = f"r{i + 1}"
    OUT.mkdir(parents=True, exist_ok=True)
    summary = {}
    for ph in (3, 4, 5):
        events = aggregate(per_fight, ph, N)
        groups = order_groups(events, N)
        for e in events:
            e.pop("_t", None)
        durs = [fr[ph]["duration"] for fr in per_fight]
        doc = {"encounterId": 1085, "territoryId": 1363, "phase": ph, "phaseName": PHASES[ph],
               "time_origin": "phase start (fight.phaseTransitions), seconds",
               "fights": [{"id": m["id"], "group": m["group"], "speed_rank": m["rank"], "phase_duration_s": r3(d)}
                          for m, d in zip(meta, durs)],
               "phase_duration_s": {"median": r3(med(durs)), "min": r3(min(durs)), "max": r3(max(durs))},
               "order_groups": groups, "events": events}
        (OUT / f"dmu-p{ph}.json").write_text(json.dumps(doc, separators=(",", ":"), ensure_ascii=True), encoding="utf-8")
        summary[ph] = len(events)
    (HERE / ".cache").mkdir(exist_ok=True)
    (HERE / ".cache" / "run_meta.json").write_text(json.dumps({"meta": meta, "rejected": dict(rejected)}), encoding="utf-8")
    print("events per phase", summary)


if __name__ == "__main__":
    main()
