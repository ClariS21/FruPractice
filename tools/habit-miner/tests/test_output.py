import json
import tempfile
import unittest
from pathlib import Path
from habitminer.output import quantiles, build_habits, write_json_atomic, summary_markdown


class OutputTests(unittest.TestCase):
    def test_quantiles(self):
        self.assertEqual(len(quantiles(list(range(20)))), 19)
        self.assertEqual(quantiles([7, 4, 3, 1, 6, 2, 5]), [1, 2, 3, 4, 5, 6, 7])
        self.assertIsNone(quantiles([1, 2, 3, 4]))

    def test_stable_private_output(self):
        metrics = {"reaction": {"debuff": [1] * 5, "cast": [], "resolve": []}, "slackUse": [],
                   "path": {"bulge": [], "pauseChance": None, "pause": [], "overshootChance": None, "overshoot": []},
                   "settle": [1] * 5, "idle": {"rate": None, "step": []},
                   "uptime": {"centreDistance": {}, "angle": [], "switchRate": None, "castStill": None}}
        profiles = [{"group": "speed", "role": "healer", "key": "ABCDEF123", "actor_id": 9001,
                     "name": "Test Player", "metrics": metrics}]
        counts = {"speed": {"clears": 1}, "decent": {"clears": 0}}
        a = build_habits(profiles, counts, "2026-11-16T00:00:00Z")
        b = build_habits(profiles, counts, "2026-11-16T00:00:00Z")
        self.assertEqual(a, b)
        self.assertEqual(a["profiles"][0]["id"], "p0001")
        summary = summary_markdown(a, {"thin": 0})
        for private in ("Test Player", "ABCDEF123", "9001"):
            self.assertNotIn(private, json.dumps(a))
            self.assertNotIn(private, summary)

    def test_atomic(self):
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "habits.json"
            write_json_atomic({"x": 1}, path)
            self.assertEqual(json.loads(path.read_text()), {"x": 1})
            self.assertFalse(Path(str(path) + ".tmp").exists())

    def test_thin_count(self):
        counts = {"speed": {"clears": 1}, "decent": {"clears": 0}, "drops": {}}
        build_habits([{"group": "speed", "role": "healer", "key": "x", "actor_id": 1,
                       "metrics": {"reaction": {"debuff": [1]}, "settle": []}}], counts, "2026-11-16T00:00:00Z")
        self.assertEqual(counts["drops"]["thin"], 1)
