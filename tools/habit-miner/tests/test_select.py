import random
import unittest
from habitminer.select import (parse_rankings, pick_speed, pick_decent_candidates,
                               fetch_all_rankings, parse_char_rankings, fetch_decent_pool)


class SelectTests(unittest.TestCase):
    def test_parse(self):
        page = {"rankings": [{"report": {"code": "A", "fightID": 3}}, {"report": None}, {"report": {"code": "A", "fightID": 3}}]}
        self.assertEqual(parse_rankings(page), [("A", 3)])

    def test_picks(self):
        ranked = [(str(i), i) for i in range(10)]
        self.assertEqual(pick_speed(ranked, 2), ranked[:2])
        a = pick_decent_candidates(ranked, 2, random.Random(1079))
        self.assertEqual(set(a), set(ranked[2:5]))
        self.assertEqual(a, pick_decent_candidates(ranked, 2, random.Random(1079)))

    def test_pages(self):
        class Fake:
            def __init__(self): self.pages = []
            def gql(self, query, variables):
                self.pages.append(variables["page"])
                return {"worldData": {"encounter": {"fightRankings": {"rankings": [{"report": {"code": "A", "fightID": variables["page"]}}], "hasMorePages": variables["page"] == 1}}}}
        fake = Fake()
        self.assertEqual(fetch_all_rankings(fake), [("A", 1), ("A", 2)])
        self.assertEqual(fake.pages, [1, 2])

    def test_parse_char_rankings_skips_missing_report_or_duration(self):
        page = {"rankings": [
            {"report": {"code": "A", "fightID": 1}, "duration": 1000, "name": "Test Player"},
            {"report": None, "duration": 1200},
            {"report": {"code": "B", "fightID": 2}},
            {"report": {"code": "C"}, "duration": 1400}]}
        self.assertEqual(parse_char_rankings(page), [("A", 1, 1000)])

    def test_decent_pool_faster_half_excludes_speed_and_is_deterministic(self):
        class Fake:
            def __init__(self): self.calls = []
            def gql(self, query, variables):
                self.calls.append((variables["spec"], variables["page"]))
                self.assert_query(query)
                entries = {
                    ("Scholar", 3): [{"report": {"code": "A", "fightID": 1}, "duration": 1000, "name": "Test Player"},
                                     {"report": {"code": "B", "fightID": 2}, "duration": 3000}],
                    ("Scholar", 4): [{"report": {"code": "C", "fightID": 3}, "duration": 2000},
                                     {"report": {"code": "S", "fightID": 9}, "duration": 900}],
                    ("Dragoon", 3): [{"report": {"code": "D", "fightID": 4}, "duration": 4000},
                                     {"report": {"code": "A", "fightID": 1}, "duration": 1000}],
                    ("Dragoon", 4): []}
                return {"worldData": {"encounter": {"characterRankings": {"rankings": entries[variables["spec"], variables["page"]], "hasMorePages": True}}}}
            def assert_query(self, query):
                if "characterRankings(specName: $spec, metric: dps, page: $page)" not in query:
                    raise AssertionError("wrong query")
        first, second = Fake(), Fake()
        args = {"exclude": {("S", 9)}, "specs": ("Scholar", "Dragoon"),
                "pages_per_spec": 2, "page_range": (3, 4)}
        a = fetch_decent_pool(first, random.Random(1079), **args)
        b = fetch_decent_pool(second, random.Random(1079), **args)
        self.assertEqual(a, b)
        self.assertEqual(set(a), {("A", 1), ("C", 3)})
        self.assertEqual(len(first.calls), 4)
        self.assertEqual({page for spec, page in first.calls if spec == "Scholar"}, {3, 4})
        self.assertNotIn("Test Player", str(a))
