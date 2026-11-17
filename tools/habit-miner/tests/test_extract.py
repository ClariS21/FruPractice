import json
import unittest
from habitminer.extract import EVENT_SETS, fetch_clear, normalise


class ExtractTests(unittest.TestCase):
    def setUp(self):
        self.meta = {"code": "ABCDEF123", "fightID": 9, "fight": {"kill": True, "startTime": 1000, "endTime": 11000,
            "phaseTransitions": [{"id": 3, "startTime": 2000}, {"id": 4, "startTime": 5000}, {"id": 5, "startTime": 8000}],
            "friendlyPlayers": [1, 2]}, "masterData": {"actors": [
            {"id": 1, "name": "Test Player", "type": "Player", "subType": "Scholar"}, {"id": 2, "name": "Other Player", "type": "Player", "subType": "Scholar"},
            {"id": 8, "name": "Boss", "type": "NPC", "subType": "Boss"}], "abilities": [{"gameID": 10, "name": "Attack"}, {"gameID": 11, "name": "Raidwide"}]}}

    def test_windows_and_players(self):
        record = normalise(self.meta, {}, "speed")
        self.assertEqual(record["phases"], {"3": [1.0, 4.0], "4": [4.0, 7.0], "5": [7.0, 10.0]})
        self.assertEqual(set(record["players"]), {1, 2})
        self.assertEqual(record["players"][1], record["players"][2])

    def test_missing_phase(self):
        self.meta["fight"]["phaseTransitions"].pop(1)
        self.assertIsNone(normalise(self.meta, {}, "speed"))

    def test_names_and_attack(self):
        pages = {"Casts/Enemies": [{"timestamp": 3000, "type": "cast", "sourceID": 8, "abilityGameID": 10},
                                    {"timestamp": 3500, "type": "begincast", "sourceID": 8, "abilityGameID": 11}],
                 "Casts/Friendlies": [{"timestamp": 3000, "type": "cast", "sourceID": 1,
                    "sourceResources": {"x": 10000, "y": 10100, "facing": 100}, "sourceName": "Test Player"}]}
        record = normalise(self.meta, pages, "speed")
        self.assertNotIn("Test Player", json.dumps(record))
        self.assertEqual(len(record["enemyCasts"]), 1)
        self.assertEqual(record["samples"][1][0][1:3], [0.0, 1.0])

    def test_player_target_never_becomes_boss_name(self):
        self.meta["masterData"]["actors"].append({"id": 9, "name": "Test Player", "type": "Player", "subType": "Scholar"})
        pages = {"DamageDone/Friendlies": [{"timestamp": 3000, "sourceID": 1, "targetID": 9,
                  "sourceResources": {"x": 10000, "y": 10000}, "targetResources": {"x": 10100, "y": 10000}}]}
        record = normalise(self.meta, pages, "speed")
        self.assertEqual(record["playerHits"][1], [])
        self.assertNotIn("Test Player", json.dumps(record))

    def test_empty_event_pages_and_last_phase_uses_fight_end(self):
        meta = self.meta
        class FakeClient:
            def __init__(self): self.events = 0
            def gql(self, query, variables):
                if "masterData" in query:
                    return {"reportData": {"report": {"fights": [meta["fight"]], "masterData": meta["masterData"]}}}
                self.events += 1
                return {"reportData": {"report": {"events": {"data": [], "nextPageTimestamp": None}}}}
        client = FakeClient()
        record = fetch_clear(client, "ABCDEF123", 9, "speed")
        self.assertEqual(client.events, len(EVENT_SETS))
        self.assertEqual(record["phases"]["5"], [7.0, 10.0])
        self.assertEqual(record["samples"], {1: [], 2: []})

    def test_null_resources_are_skipped(self):
        pages = {"Casts/Friendlies": [{"timestamp": 3000, "type": "cast", "sourceID": 1,
                                       "sourceResources": None, "targetResources": None}],
                 "DamageDone/Friendlies": [{"timestamp": 4000, "sourceID": 1, "targetID": 8,
                                            "sourceResources": None, "targetResources": None}],
                 "Casts/Enemies": [{"timestamp": 5000, "sourceID": 8, "type": "cast", "abilityGameID": 11,
                                     "sourceResources": None}]}
        record = normalise(self.meta, pages, "speed")
        self.assertEqual(record["samples"][1], [])
        self.assertEqual(record["playerHits"][1], [])
        self.assertEqual(record["enemyCasts"][0][-3:], [None, None, None])

    def test_missing_facing_at_same_position_and_time(self):
        pages = {"Casts/Friendlies": [
            {"timestamp": 3000, "type": "cast", "sourceID": 1,
             "sourceResources": {"x": 10000, "y": 10000, "facing": None}},
            {"timestamp": 3000, "type": "cast", "sourceID": 1,
             "sourceResources": {"x": 10000, "y": 10000, "facing": 100}}]}
        record = normalise(self.meta, pages, "speed")
        self.assertEqual(len(record["samples"][1]), 2)

    def test_unnamed_npc_hit_is_skipped(self):
        self.meta["masterData"]["actors"][2]["name"] = None
        pages = {"DamageDone/Friendlies": [{"timestamp": 3000, "sourceID": 1, "targetID": 8,
                  "sourceResources": {"x": 10000, "y": 10000},
                  "targetResources": {"x": 10100, "y": 10000}}]}
        self.assertEqual(normalise(self.meta, pages, "speed")["playerHits"][1], [])

    def test_applied_effect_records_enemy_source(self):
        self.meta["masterData"]["actors"].extend([
            {"id": 9, "name": "Other Player", "type": "Player", "subType": "Scholar"},
            {"id": 10, "name": "Pet", "type": "Pet"},
            {"id": 11, "name": "Limit Break", "type": "LimitBreak"}])
        pages = {"Debuffs/Friendlies": [
            {"timestamp": 3000, "type": "applydebuff", "sourceID": source, "targetID": 1, "abilityGameID": source}
            for source in (8, 1, 9, 10, 11)]}
        record = normalise(self.meta, pages, "speed")
        self.assertEqual({row[1]: row[3] for row in record["applied"][1]},
                         {8: True, 1: False, 9: False, 10: False, 11: False})

    def test_unresolved_friendly_source_is_not_enemy(self):
        self.meta["fight"]["friendlyPlayers"].append(12)
        pages = {"Debuffs/Friendlies": [{"timestamp": 3000, "type": "applydebuff",
                                         "sourceID": 12, "targetID": 1, "abilityGameID": 77}]}
        self.assertFalse(normalise(self.meta, pages, "speed")["applied"][1][0][3])
