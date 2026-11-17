import unittest
from habitminer.cues import attach


class CueTests(unittest.TestCase):
    def setUp(self): self.move = {"t0": 12.0, "t1": 13.0}

    def test_latest(self):
        result = attach(self.move, [[10, 1, "debuff", True]], [[11, "begincast", 2], [16, "cast", 2]])
        self.assertEqual(result["cue"], {"t": 11, "type": "cast"})
        self.assertEqual(result["deadline"], 16)
        self.assertAlmostEqual(result["slack"], 3.2)

    def test_debuff(self):
        self.assertEqual(attach(self.move, [[10, 1, "debuff", True]], [])["cue"], {"t": 10, "type": "debuff"})

    def test_old_cue_and_late_deadline(self):
        result = attach(self.move, [[5, 1, "debuff", True]], [[22, "cast", 1]])
        self.assertIsNone(result["cue"])
        self.assertIsNone(result["deadline"])

    def test_party_buff_does_not_hide_enemy_debuff(self):
        applied = [[10, 1, "debuff", True], [11.8, 2, "buff", False],
                   [11.7, 3, "debuff", False], [11.6, 4, "buff", True]]
        self.assertEqual(attach(self.move, applied, [])["cue"], {"t": 10, "type": "debuff"})

    def test_helper_cast_is_not_a_resolve_cue(self):
        self.assertIsNone(attach(self.move, [], [[11, "cast", 10]], [])["cue"])
        self.assertEqual(attach(self.move, [], [[11, "cast", 10]], [[12, 10, 1]])["cue"],
                         {"t": 11, "type": "resolve"})
        self.assertIsNone(attach(self.move, [], [[11, "cast", 10]], [[12.6, 10, 1]])["cue"])
