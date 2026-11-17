import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import mine


class MineTests(unittest.TestCase):
    def test_player_with_no_samples_processes_without_metrics(self):
        record = {"key": "x", "group": "speed", "phases": {"3": [0, 10], "4": [10, 20], "5": [20, 30]},
                  "players": {1: {"job": "Scholar", "role": "healer"}}, "samples": {1: []},
                  "playerCasts": {1: []}, "applied": {1: []}, "failDebuffs": [],
                  "enemyCasts": [], "playerHits": {1: []}, "damageTaken": [], "deaths": []}
        drops = {key: 0 for key in ("dead", "dirty", "forced")}
        players, rests = mine._collect_clear(record, drops)
        self.assertEqual(players[1]["moves"], [])
        self.assertEqual(rests, [])

    def test_cli_pipeline_is_offline_with_fakes(self):
        record = {"key": "private-key", "group": "speed", "phases": {"3": [0, 10], "4": [10, 20], "5": [20, 30]},
                  "players": {1: {"job": "Scholar", "role": "healer"}},
                  "samples": {1: [[0, 0, 0, 0], [1, 0, 0, 0], [2, 0, 0, 0]]},
                  "playerCasts": {1: []}, "applied": {1: []}, "failDebuffs": [],
                  "enemyCasts": [], "playerHits": {1: []}, "damageTaken": [], "deaths": []}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            out_json, out_summary = root / "habits.json", root / "summary.md"
            args = ["--creds", str(root / "unused"), "--speed", "1", "--decent", "0",
                    "--out-json", str(out_json), "--out-summary", str(out_summary)]
            printed = io.StringIO()
            with patch.object(mine, "Client"), patch.object(mine, "fetch_all_rankings", return_value=[("ABCDEF123", 1)]), \
                 patch.object(mine, "fetch_clear", return_value=record) as fetch, contextlib.redirect_stdout(printed):
                self.assertEqual(mine.main(args), 0)
            self.assertEqual(fetch.call_count, 1)
            self.assertEqual(json.loads(out_json.read_text())["schemaVersion"], 1)
            self.assertIn("thin: 1", out_summary.read_text())
            for private in ("ABCDEF123", "private-key"):
                self.assertNotIn(private, printed.getvalue())
                self.assertNotIn(private, out_json.read_text())
                self.assertNotIn(private, out_summary.read_text())
            self.assertFalse(Path(str(out_summary) + ".tmp").exists())

    def test_decent_uses_character_pool_and_stops_at_requested_clear_count(self):
        record = {"key": "fixture", "group": "speed", "phases": {"3": [0, 10], "4": [10, 20], "5": [20, 30]},
                  "players": {}, "samples": {}, "playerCasts": {}, "applied": {}, "failDebuffs": [],
                  "enemyCasts": [], "playerHits": {}, "damageTaken": [], "deaths": []}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            args = ["--creds", str(root / "unused"), "--speed", "1", "--decent", "1",
                    "--out-json", str(root / "habits.json"), "--out-summary", str(root / "summary.md")]
            printed = io.StringIO()
            def fake_clear(client, code, fight, group):
                return {**record, "group": group}
            with patch.object(mine, "Client"), patch.object(mine, "fetch_all_rankings", return_value=[("S", 1)]), \
                 patch.object(mine, "fetch_decent_pool", return_value=[("D", 2), ("E", 3)]) as pool, \
                 patch.object(mine, "fetch_clear", side_effect=fake_clear) as fetch, contextlib.redirect_stdout(printed):
                mine.main(args)
            self.assertEqual(fetch.call_count, 2)
            self.assertEqual(fetch.call_args.args[1:3], ("D", 2))
            self.assertEqual(pool.call_args.kwargs["exclude"], {("S", 1)})
            self.assertIn("decent pool: 2 candidates", printed.getvalue())
