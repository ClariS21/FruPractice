"""Generate docs/dmu/timelines/README.md from the three phase JSON files."""
import json, collections, statistics
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE.parent.parent / "docs" / "dmu" / "timelines"
NOISE = ("Vulnerability Up", "Resistance Down", "Damage Down", "Weakness", "Brink of Death")


def f(x, nd=1):
    return "" if x is None else f"{x:.{nd}f}"


def stat_cell(s, nd=1):
    if not s:
        return ""
    return f"{s['median']:.{nd}f}" if s["max"] - s["min"] <= 2 else f"{s['median']:.{nd}f} ({s['min']:.1f}-{s['max']:.1f})"


def modal(lists):
    c = collections.Counter(tuple(x) for x in lists if x)
    return ", ".join(c.most_common(1)[0][0]) if c else ""


def role_summary(lists):
    # compress recipients to a short text
    c = collections.Counter(tuple(x) for x in lists if x)
    if not c:
        return ""
    top, n = c.most_common(1)[0]
    if len(top) > 4:
        return f"{len(top)} players"
    if n < 0.6 * sum(c.values()):
        return "random " + ("player" if len(top) == 1 else f"{len(top)} players")
    return "/".join(top)


def rows_for(d, N):
    rows = []
    cast_ids = {e["id"] for e in d["events"] if e["kind"] == "cast"}
    for e in d["events"]:
        k = e["kind"]
        t = e["median_t"]
        if e.get("high_frequency"):
            continue
        if e["fights_present"] < max(2, N // 2):
            continue
        if k == "cast":
            notes = []
            if e["source"]["name"]:
                notes.append(e["source"]["name"])
            tg = role_summary(e["targets"])
            if tg:
                notes.append("tgt " + tg)
            if (e["n_sources_median"] or 1) > 1:
                notes.append(f"x{int(e['n_sources_median'])} casters")
            if e["resolve"] and e["begincast"]:
                notes.append(f"resolve {stat_cell(e['resolve'])}")
            if e["flags"]:
                notes.append("VARIES" if any("varies" in x for x in e["flags"]) else "")
            ct = e["cast_time_s"]["median"] if e["cast_time_s"] else None
            dm = e["damage"]
            rows.append((t, "cast", f"{e['name']} ({e['id']}) #{e['k']}", f(ct), stat_cell(dm) if dm else "", "; ".join(x for x in notes if x)))
        elif k == "damage":
            if e["id"] in cast_ids:
                continue
            u = e["unmit"]
            note = f"{e['source']['name']}; hits {e['hits_per_cluster_median']:.0f}/{e['targets_hit_median']:.0f} tgts; unmit {u['min']}-{u['max']}" if u else ""
            if e["lethal_hits"]:
                note += "; LETHAL hits"
            if e["flags"] and any("varies" in x for x in e["flags"]):
                note += "; VARIES"
            rows.append((t, "dmg", f"{e['name']} ({e['id']}) #{e['k']}", "", stat_cell(e["time"]), note))
        elif k == "status":
            if any(n in (e["name"] or "") for n in NOISE):
                continue
            ds = e["duration_field_s"]
            note = f"{e['status_type']} on {role_summary(e['recipients'])} (n={e['recipients_n_median']:.0f})"
            if ds:
                note += f"; dur {ds:.0f}s"
            if e["max_stacks"] > 1:
                note += f"; stacks {e['max_stacks']}"
            if any("varies" in x for x in e["flags"]):
                note += "; VARIES"
            rows.append((t, "status", f"{e['name']} (st {e['id'] - 1000000 if e['id'] >= 1000000 else e['id']}) #{e['k']}", "", stat_cell(e["apply"]), note))
        elif k == "headmarker":
            rows.append((t, "marker", f"headmarker {e['id']} #{e['k']}", "", "", f"on {role_summary(e['recipients'])}; fights {e['fights_present']}"))
        elif k == "tether":
            rows.append((t, "tether", f"tether {e['id']} #{e['k']}", "", "", f"{modal(e['ends'])}; fights {e['fights_present']}"))
        elif k == "targetable":
            nm, gid, st = e["id"].split("|")
            lab = {"1": "targetable", "0": "untargetable", "death": "dies"}.get(st, st)
            rows.append((t, "npc", f"{nm} ({gid}) {lab}", "", "", f"fights {e['fights_present']}"))
    rows.sort(key=lambda r: r[0])
    return rows


def variant_slots(d, N):
    """Group partially-present casts that begin at about the same time: same slot, different variant ids."""
    ev = [e for e in d["events"] if e["kind"] == "cast" and e["begincast"] and e["fights_present"] < N
          and e["begincast"]["max"] - e["begincast"]["min"] <= 2.0]
    ev.sort(key=lambda e: e["begincast"]["median"])
    slots, cur = [], []
    for e in ev:
        if cur and e["begincast"]["median"] - cur[-1]["begincast"]["median"] > 1.0:
            slots.append(cur)
            cur = []
        cur.append(e)
    if cur:
        slots.append(cur)
    out = []
    for sl in slots:
        out.append({"t": round(statistics.median([e["begincast"]["median"] for e in sl]), 3),
                    "variants": [{"name": e["name"], "id": e["id"], "k": e["k"], "fights": e["fights_present"]} for e in sl]})
    return out


def main():
    docs = {p: json.load(open(OUT / f"dmu-p{p}.json", encoding="utf-8")) for p in (3, 4, 5)}
    N = len(docs[3]["fights"])
    for p in (3, 4, 5):
        docs[p]["variant_slots"] = variant_slots(docs[p], N)
        (OUT / f"dmu-p{p}.json").write_text(json.dumps(docs[p], separators=(",", ":"), ensure_ascii=True), encoding="utf-8")
    meta = docs[3]["fights"]
    L = []
    L.append("# DMU P3-P5 real fight timelines\n")
    L.append("Source: public FFLogs speed-kill clears of Dancing Mad (Ultimate), patch 7.51. Generated by `tools/dmu-timeline/` "
             "(`analyze.py` then `readme.py`). Reports are anonymised (`r1`..); players are referred to by job only.\n")
    L.append("## Encounter\n")
    L.append("- FFLogs encounter id **1085** (zone 76 `Dancing Mad`). The in-log `encounterend` event carries game encounter id 1363 (territory).")
    L.append("- Phase ids from the report `phases` data: **P1** Kefka, **P2** Forsaken Kefka, **P3 = id 3 `P3: Exdeath and Chaos`**, "
             "**P4 = id 4 `P4: Kefka Says`**, **P5 = id 5 `P5: Ultima Kefka`**. No intermission phases.")
    L.append("- Phase delimiting: `fight.phaseTransitions[id].startTime`. P3 = [start of 3, start of 4), P4 = [start of 4, start of 5), P5 = [start of 5, fight end). t = 0 is the phase start. "
             "P5 ends at the kill.\n")
    top = sum(1 for m in meta if m["group"] == "top")
    L.append("## Selection\n")
    L.append(f"- **{N} fights** used: {top} from the top of the speed rankings and {N - top} from deeper ranking pages (rank > 100, random draw).")
    L.append("- Rankings paged 12 pages x 50 (600 ranked kills). Candidates were pre-filtered to kills whose ranking entry reports 0 deaths and size 8; "
             "then verified from the event stream: all of P3, P4, P5 present, kill, and no player `death` event from P3 start to the kill.")
    L.append(f"- Result: 0 of {N} candidates were rejected by the event-level check (the ranking `deaths` pre-filter already removed them).")
    L.append("- Events: one `dataType: All` query per fight over P3 start..kill with a filter that keeps enemy-sourced cast/begincast/damage/status/tether events plus headmarker, death, targetabilityupdate.")
    L.append("- Clustering: same-ability events within 0.5 s of the first one (damage: 0.7 s) are one occurrence, so a mechanic hitting from several clones counts once (`x N casters`). "
             "Occurrence k is the k-th cluster of that ability id in the phase, per fight.")
    L.append("- Statuses: only those applied to players by an enemy source. Headmarker ids are raw `markerID` values. Statuses applied on nearly every hit (`Vulnerability Up`, `Resistance Down`, ...) "
             "stay in the JSON but are omitted from the tables below.")
    L.append("- Cast time = `begincast` duration. Blank = instant cast (resolve only). Damage time = first player-damage cluster of the same ability id at or after the resolve. "
             "Many mechanics cast one ability id and damage under another, so those appear as separate `dmg` rows.")
    L.append("- A time shown as `m (lo-hi)` varies by more than 2 s across fights.\n")
    L.append("## Phase durations (seconds)\n")
    L.append("| Phase | median | min | max |\n|---|---|---|---|")
    for p in (3, 4, 5):
        d = docs[p]["phase_duration_s"]
        L.append(f"| P{p} | {d['median']:.1f} | {d['min']:.1f} | {d['max']:.1f} |")
    L.append("")
    for p in (3, 4, 5):
        d = docs[p]
        L.append(f"## P{p}: {d['phaseName'].split(': ', 1)[1]}\n")
        L.append(f"Machine-readable: `dmu-p{p}.json` ({len(d['events'])} aggregated events, per-fight raw times in each).\n")
        L.append("| t (s) | type | ability | cast (s) | damage/apply t | notes |\n|---|---|---|---|---|---|")
        for t, ty, ab, ct, dm, notes in rows_for(d, N):
            L.append(f"| {t:.1f} | {ty} | {ab} | {ct} | {dm} | {notes.replace('|', '/')} |")
        L.append("")
        # variance
        L.append(f"### P{p} variance flags\n")
        vary = [e for e in d["events"] if not e.get("high_frequency") and e["kind"] in ("cast", "damage", "status", "headmarker", "tether")
                and any("varies" in x for x in e["flags"]) and e["fights_present"] >= 3
                and not any(n in (e["name"] or "") for n in NOISE)]
        sub = [e for e in d["events"] if e["kind"] in ("cast", "status", "headmarker", "tether", "damage") and not e.get("high_frequency")
               and e["fights_present"] < N and e["fights_present"] >= 3 and e["fights_present"] >= N * 0.5
               and not any(n in (e["name"] or "") for n in NOISE)]
        L.append(f"- Events with a >2 s spread across fights (occurrence-matched, so random order shows up here): **{len(vary)}**. First 40:\n")
        for e in vary[:40]:
            nm = e.get("name") or f"{e['kind']} {e['id']}"
            fl = next(x for x in e["flags"] if "varies" in x)
            L.append(f"  - {e['median_t']:.1f}s {e['kind']} {nm} #{e['k']}: {fl}")
        L.append(f"- Events present in only some fights (50-99% of fights, i.e. a variant branch): **{len(sub)}**. First 25:\n")
        for e in sub[:25]:
            nm = e.get("name") or f"{e['kind']} {e['id']}"
            L.append(f"  - {e['median_t']:.1f}s {e['kind']} {nm} #{e['k']}: in {e['fights_present']}/{N}")
        L.append(f"- Variant slots (partially-present casts beginning at the same time; same timing, different ability id per fight): **{len(d['variant_slots'])}**. First 25:\n")
        for sl in d["variant_slots"][:25]:
            L.append(f"  - {sl['t']:.1f}s: " + " | ".join(f"{v['name']} ({v['id']}) #{v['k']} in {v['fights']}" for v in sl["variants"][:6]))
        L.append(f"- Relative-order groups (events within 6 s whose order differs in >=10% of fights): **{len(d['order_groups'])}**. Full detail in `order_groups` of the JSON; summary:\n")
        for g in d["order_groups"][:25]:
            top_o = g["orders_seen"][0] if g["orders_seen"] else None
            L.append(f"  - ~{min(g['median_times']):.0f}s, {len(g['members'])} events, {len(g['orders_seen'])} distinct orders seen"
                     + (f" (commonest in {top_o['fights']} fights)" if top_o else "") + f": " + "; ".join(m.split('[')[0] for m in g["members"][:6]))
        L.append("")
    L.append("## Reading the variance, and anything surprising\n")
    L += [
        "- Timing inside a branch is scripted, not HP-push dependent: the same casts begin at the same second in every fight (spread < 0.2 s for most). "
        "Almost every >2 s flag is an occurrence-matching artefact of a random variant: the same ability id appears in a different slot, or a different id fills the slot "
        "(`variant_slots`). Treat a flag as 'which variant', not 'jitter'.",
        "- P3: the ~59 s and ~239 s slots each hold either Longitudinal (47869) or Latitudinal (47870) Implosion, and the ~142/173/241 s Slap Happy slots hold 47846 or 47847, chosen per fight; "
        "24 distinct long-cast sequences in 27 fights. The ~42-70 s block (Stray Flames / Inferno / Stray Spray / Tsunami) and the ~91 s Cyclone are order-randomized elemental casts "
        "(Stray Flames resolves at 41.6 or 68.8 depending on fight). Thunder III 47884 single-target hits are a rotating tank-buster-like sequence; their damage times split into early and late sets because of the same swap.",
        "- P4 is the most regular: a fixed ~163 s (163.0-163.8 s in all 27 fights), Kefka Says cast at ~5 s followed by three 15 s rounds (casts at ~15/30/45 s). The only randomization is the order of Tsunami (20 s) and Inferno (35 s), "
        "which swap between fights (2 variants), and which headmarker (675-678) goes on which player. Later (~78 s, ~91 s, ~114 s) ids vary (Thrumming Thunder III 47775-7, Stray Flames 47906/47907, Stray Spray 47908/47909).",
        "- P4 ends on a timer (Kefka untargetable ~132 s, last Ultima Upsurge ~127 s begin) rather than on HP, so the sim can chain it at fixed length.",
        "- P5: 2 main sequences (Catastrophic Choice #2 id 49742 vs 49743 at ~76 s). The phase ends with Forsaken Null (25.7 s cast, begin ~194 s, would resolve ~220 s): clears kill the boss before it resolves, "
        "so P5 length (208-221 s) is DPS-dependent. Stray Apocalypse hits at 2.5 s intervals (~100-112 s) with variable pairing.",
        "- P3 ends when Chaos/Exdeath become untargetable (~291-292 s) while their last casts (Blizzard III / Big Bang at ~283-288 s) are still resolving; P3 length varies 296-310 s with DPS.",
        "- Unnamed ability ids come through as `unknown_xxxx` from FFLogs masterData (for example 50516/50517 Kefka in P3).",
        "- Raw arrays in the JSON (`raw`, `targets`, `recipients`, `ends`) are aligned to the `fights` list; `null` = that fight had no k-th occurrence. Event kinds: `cast`, `damage`, `status`, `headmarker`, `tether`, `targetable` (targetability and enemy deaths), `npc_seen` (first event from an NPC in the phase, a proxy for spawn).",
        "- API limits: none hit. 12 ranking pages plus 27 fights (about 55 queries) ran in roughly 30 s with the client's cache and no rate-limit retries.",
    ]
    (OUT / "README.md").write_text("\n".join(L) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
