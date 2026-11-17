import math
import statistics


RANKINGS_QUERY = """query($page: Int!) {
  worldData { encounter(id: 1079) {
    fightRankings(metric: speed, page: $page)
  } }
}"""
CHAR_RANKINGS_QUERY = """query($spec: String!, $page: Int!) {
  worldData { encounter(id: 1079) {
    characterRankings(specName: $spec, metric: dps, page: $page)
  } }
}"""


def parse_rankings(page_json: dict) -> list[tuple[str, int]]:
    seen = set()
    result = []
    for entry in page_json.get("rankings", []):
        report = entry.get("report")
        if not report or not report.get("code") or report.get("fightID") is None:
            continue
        pair = (report["code"], report["fightID"])
        if pair not in seen:
            result.append(pair)
            seen.add(pair)
    return result


def pick_speed(ranked, n):
    return ranked[:n]


def parse_char_rankings(page_json: dict) -> list[tuple[str, int, int]]:
    result = []
    for entry in page_json.get("rankings", []):
        report = entry.get("report")
        duration = entry.get("duration")
        if not report or not report.get("code") or report.get("fightID") is None or duration is None:
            continue
        result.append((report["code"], report["fightID"], int(duration)))
    return result


def fetch_decent_pool(client, rng, exclude: set[tuple[str, int]],
                      specs=("Scholar", "WhiteMage", "Dragoon", "Machinist", "BlackMage", "Paladin"),
                      pages_per_spec=6, page_range=(3, 60)) -> list[tuple[str, int]]:
    collected = {}
    for spec in specs:
        # Treat both page_range endpoints as page numbers that may be sampled.
        for page in rng.sample(range(page_range[0], page_range[1] + 1), pages_per_spec):
            data = client.gql(CHAR_RANKINGS_QUERY, {"spec": spec, "page": page})["worldData"]["encounter"]["characterRankings"]
            if not data or not data.get("rankings"):
                continue
            for code, fight, duration in parse_char_rankings(data):
                pair = (code, fight)
                if pair not in exclude:
                    collected.setdefault(pair, duration)
    if not collected:
        return []
    median = statistics.median(collected.values())
    candidates = [pair for pair, duration in collected.items() if duration <= median]
    rng.shuffle(candidates)
    return candidates


def pick_decent_candidates(ranked, n_speed, rng):
    candidates = ranked[n_speed:math.ceil(len(ranked) / 2)]
    rng.shuffle(candidates)
    return candidates


def fetch_all_rankings(client):
    result = []
    seen = set()
    for page in range(1, 201):
        data = client.gql(RANKINGS_QUERY, {"page": page})["worldData"]["encounter"]["fightRankings"]
        for pair in parse_rankings(data):
            if pair not in seen:
                result.append(pair)
                seen.add(pair)
        if not data["hasMorePages"]:
            break
    return result
