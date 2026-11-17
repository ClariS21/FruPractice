import hashlib
import sys


JOBS = {
    "tank": "Paladin Warrior DarkKnight Gunbreaker",
    "healer": "WhiteMage Scholar Astrologian Sage",
    "melee": "Monk Dragoon Ninja Samurai Reaper Viper",
    "ranged": "Bard Machinist Dancer",
    "caster": "BlackMage Summoner RedMage Pictomancer",
}
ROLE = {job: role for role, jobs in JOBS.items() for job in jobs.split()}
EVENT_SETS = [(kind, host) for kind, host in (("Casts", "Friendlies"), ("DamageDone", "Friendlies"),
    ("DamageTaken", "Friendlies"), ("Healing", "Friendlies"), ("Debuffs", "Friendlies"),
    ("Buffs", "Friendlies"), ("Casts", "Enemies"), ("Deaths", "Friendlies"))]
META_QUERY = """query($code: String!, $fight: Int!) { reportData { report(code: $code) {
  fights(fightIDs: [$fight]) { id kill startTime endTime phaseTransitions { id startTime } friendlyPlayers }
  masterData { actors { id name type subType } abilities { gameID name } }
} } }"""


def _position(resource):
    if not resource or resource.get("x") is None or resource.get("y") is None:
        return None
    return [resource["x"] / 100 - 100, resource["y"] / 100 - 100,
            None if resource.get("facing") is None else resource["facing"] / 100]


def normalise(meta: dict, pages: dict[str, list[dict]], group: str) -> dict | None:
    fight = meta["fight"]
    if not fight.get("kill"):
        print("Skipping non-kill clear", file=sys.stderr)
        return None
    transitions = {p["id"]: p["startTime"] for p in fight.get("phaseTransitions", [])}
    if not all(p in transitions for p in (3, 4, 5)):
        print("Skipping clear: missing phase transition", file=sys.stderr)
        return None
    start = fight["startTime"]
    sec = lambda stamp: (stamp - start) / 1000
    phases = {"3": [sec(transitions[3]), sec(transitions[4])],
              "4": [sec(transitions[4]), sec(transitions[5])],
              "5": [sec(transitions[5]), sec(fight["endTime"])]}
    actors = {a["id"]: a for a in meta["masterData"]["actors"]}
    abilities = {a["gameID"]: a["name"] for a in meta["masterData"]["abilities"]}
    players = {aid: {"job": actors[aid]["subType"], "role": ROLE[actors[aid]["subType"]]}
               for aid in fight["friendlyPlayers"] if aid in actors and actors[aid].get("subType") in ROLE}
    record = {"key": hashlib.sha1(f'{meta["code"]}#{meta["fightID"]}'.encode()).hexdigest()[:12],
              "group": group, "phases": phases, "players": players,
              "samples": {aid: [] for aid in players}, "playerCasts": {aid: [] for aid in players},
              "applied": {aid: [] for aid in players}, "failDebuffs": sorted(k for k, v in abilities.items() if v in ("Vulnerability Up", "Damage Down")),
              "enemyCasts": [], "playerHits": {aid: [] for aid in players}, "damageTaken": [], "deaths": []}
    for kind, host in EVENT_SETS:
        for event in pages.get(f"{kind}/{host}", []):
            t = sec(event["timestamp"])
            source, target = event.get("sourceID"), event.get("targetID")
            ability = event.get("abilityGameID")
            typ = event.get("type")
            sr, tr = _position(event.get("sourceResources")), _position(event.get("targetResources"))
            if host == "Friendlies":
                for aid, position in ((source, sr), (target, tr)):
                    if aid in players and position is not None:
                        record["samples"][aid].append([t, *position])
            if kind == "Casts" and host == "Friendlies" and source in players and typ in ("begincast", "cast"):
                record["playerCasts"][source].append([t, typ, ability])
            elif kind in ("Debuffs", "Buffs") and target in players and typ in ("applydebuff", "applybuff"):
                source_type = str(actors.get(source, {}).get("type", "")).replace(" ", "").lower()
                from_enemy = source not in fight["friendlyPlayers"] and source_type not in ("player", "pet", "limitbreak")
                record["applied"][target].append([t, ability, "debuff" if typ == "applydebuff" else "buff", from_enemy])
            elif kind == "Casts" and host == "Enemies" and typ in ("begincast", "cast") and abilities.get(ability) != "Attack":
                record["enemyCasts"].append([t, typ, ability, source, *(sr or [None, None, None])])
            elif kind == "DamageDone" and source in players and target in actors and actors[target].get("type") != "Player" and actors[target].get("name") and sr and tr:
                record["playerHits"][source].append([t, actors[target]["name"], tr[0], tr[1], tr[2], sr[0], sr[1]])
            elif kind == "DamageTaken" and target in players and abilities.get(ability) != "Attack":
                record["damageTaken"].append([t, ability, target])
            elif kind == "Deaths" and target in players:
                record["deaths"].append([t, target])
    for field in ("samples", "playerCasts", "applied", "playerHits"):
        for aid, values in record[field].items():
            record[field][aid] = [list(v) for v in sorted(set(map(tuple, values)),
                                                       key=lambda row: (row[0], tuple(repr(value) for value in row[1:])))]
    for field in ("enemyCasts", "damageTaken", "deaths"):
        record[field].sort(key=lambda row: row[0])
    return record


def fetch_clear(client, code: str, fight: int, group: str) -> dict | None:
    report = client.gql(META_QUERY, {"code": code, "fight": fight})["reportData"]["report"]
    if not report or not report["fights"]:
        return None
    meta = {"code": code, "fightID": fight, "fight": report["fights"][0], "masterData": report["masterData"]}
    transitions = {p["id"]: p["startTime"] for p in meta["fight"].get("phaseTransitions", [])}
    if not meta["fight"].get("kill") or 3 not in transitions or 4 not in transitions or 5 not in transitions:
        return normalise(meta, {}, group)
    pages = {}
    for kind, host in EVENT_SETS:
        query = f"""query($code: String!, $fight: Int!, $start: Float!, $end: Float!) {{ reportData {{ report(code: $code) {{
          events(fightIDs: [$fight], dataType: {kind}, hostilityType: {host}, startTime: $start, endTime: $end,
                 includeResources: true, limit: 10000) {{ data nextPageTimestamp }}
        }} }} }}"""
        cursor = transitions[3]
        values = []
        while cursor < meta["fight"]["endTime"]:
            page = client.gql(query, {"code": code, "fight": fight, "start": cursor, "end": meta["fight"]["endTime"]})["reportData"]["report"]["events"]
            values.extend(page["data"])
            next_cursor = page.get("nextPageTimestamp")
            if next_cursor is None or next_cursor <= cursor:
                break
            cursor = next_cursor
        pages[f"{kind}/{host}"] = values
    return normalise(meta, pages, group)
