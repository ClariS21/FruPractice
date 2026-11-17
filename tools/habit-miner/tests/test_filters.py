import unittest
from habitminer.filters import dead_windows, dirty_windows, keep, trim_outliers


class FilterTests(unittest.TestCase):
    def test_dead(self):
        record = {"phases": {"3": [0, 10], "4": [10, 20], "5": [20, 30]}, "players": {1: {}}, "deaths": [[15, 1]]}
        self.assertEqual(dead_windows(record)[1], [[10, 20]])

    def test_dirty(self):
        record = {"players": {1: {"role": "healer"}, 2: {"role": "tank"}}, "damageTaken": [[10, 100, 1], [20, 100, 2]],
                  "applied": {1: [[30, 77, "debuff", True], [35, 77, "buff", False]]}, "failDebuffs": [77]}
        self.assertEqual(dirty_windows(record)[1], [[25, 32]])
        self.assertEqual(dirty_windows(record)[2], [])

    def test_single_target_hit_without_failure_debuff_is_clean(self):
        record = {"players": {1: {"role": "healer"}}, "damageTaken": [[10, 100, 1]],
                  "applied": {1: []}, "failDebuffs": [77]}
        self.assertEqual(dirty_windows(record)[1], [])

    def test_edges_and_forced(self):
        self.assertFalse(keep({"t0": 5, "t1": 10}, [[10, 15]]))
        self.assertFalse(keep({"t0": 0, "t1": 1, "forced": True}, []))
        self.assertTrue(keep({"t0": 0, "t1": 1}, [[2, 3]]))

    def test_trim(self):
        self.assertEqual(trim_outliers(list(range(101))), list(range(2, 99)))
