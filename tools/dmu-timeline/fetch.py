"""Fetch DMU (encounter 1085, territory 1363) P3-P5 fight data from FFLogs. Credentials path only."""
import os, sys, json, random
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "habit-miner"))
from habitminer.client import Client

ENC = 1085
CREDS = os.environ["FFLOGS_CREDS"]  # path to the FFLogs API credentials file; never commit it
client = Client(CREDS, str(HERE / ".cache"))

RANK_Q = """query($p:Int!){ worldData { encounter(id:1085){ fightRankings(metric: speed, page:$p) } } }"""
META_Q = """query($code:String!,$f:Int!){reportData{report(code:$code){
  fights(fightIDs:[$f]){id kill startTime endTime phaseTransitions{id startTime} friendlyPlayers}
  masterData{actors{id name type subType gameID petOwner} abilities{gameID name}} }}}"""
FILTER = ('type in ("begincast","cast","damage","applydebuff","removedebuff","applybuff","removebuff",'
          '"applybuffstack","removebuffstack","refreshbuff","refreshdebuff","tether","headmarker",'
          '"targetabilityupdate","death") and (type in ("headmarker","death","targetabilityupdate") '
          'or not source.type = "Player")')
EV_Q = """query($code:String!,$f:Int!,$s:Float!,$e:Float!,$fl:String!){reportData{report(code:$code){
  events(fightIDs:[$f],dataType:All,startTime:$s,endTime:$e,limit:10000,filterExpression:$fl){data nextPageTimestamp}}}}"""


def rankings(max_pages):
    out = []
    for p in range(1, max_pages + 1):
        d = client.gql(RANK_Q, {"p": p})["worldData"]["encounter"]["fightRankings"]
        for i, r in enumerate(d["rankings"]):
            rep = r.get("report") or {}
            if rep.get("code") and rep.get("fightID") is not None:
                out.append({"rank": (p - 1) * 50 + i + 1, "code": rep["code"], "fight": rep["fightID"],
                            "deaths": r.get("deaths"), "duration": r.get("duration"), "size": r.get("size")})
        if not d.get("hasMorePages"):
            break
    return out


def fetch_fight(code, fid):
    rep = client.gql(META_Q, {"code": code, "f": fid})["reportData"]["report"]
    if not rep or not rep["fights"]:
        return None
    fight = rep["fights"][0]
    tr = {t["id"]: t["startTime"] for t in fight.get("phaseTransitions", [])}
    if not fight.get("kill") or not all(k in tr for k in (3, 4, 5)):
        return {"skip": "no kill or missing phase"}
    cur, vals = tr[3], []
    while True:
        p = client.gql(EV_Q, {"code": code, "f": fid, "s": cur, "e": fight["endTime"], "fl": FILTER})["reportData"]["report"]["events"]
        vals += p["data"]
        nxt = p.get("nextPageTimestamp")
        if not nxt or nxt <= cur:
            break
        cur = nxt
    return {"fight": fight, "actors": rep["masterData"]["actors"], "abilities": rep["masterData"]["abilities"], "events": vals}


if __name__ == "__main__":
    r = rankings(int(sys.argv[1]))
    import collections
    print("total ranked:", len(r), "deaths dist:", dict(collections.Counter(x["deaths"] for x in r)))
