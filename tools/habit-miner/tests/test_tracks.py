import unittest
from habitminer.tracks import clean, pieces, resample, segment


class TrackTests(unittest.TestCase):
    def test_walk_and_rests(self):
        raw = [[t / 4, 0, 0, 0] for t in range(13)] + [[3.8, 5, 0, 0]] + [[3.8 + t / 4, 5, 0, 0] for t in range(1, 13)]
        moves, rests = segment(resample(raw))
        self.assertEqual(len(moves), 1)
        self.assertAlmostEqual(moves[0]["length"], 5, delta=.3)
        self.assertEqual(len(rests), 2)

    def test_gap(self):
        self.assertEqual(len(pieces([[0, 0, 0, 0], [1, 0, 0, 0], [3, 0, 0, 0]])), 2)

    def test_forced(self):
        moves, _ = segment(resample([[0, 0, 0, 0], [.5, 12, 0, 0], [1, 13, 0, 0]]))
        self.assertTrue(moves[0]["forced"])

    def test_near_simultaneous_jitter_is_not_forced_but_jump_is(self):
        ordinary = [[0, 0, 0, 0], [.25, 1, 0, 0], [.30, 1.5, 0, 0], [.55, 2.5, 0, 0], [.8, 3.5, 0, 0]]
        jump = [[0, 0, 0, 0], [.25, 1, 0, 0], [.55, 5, 0, 0], [.8, 6, 0, 0]]
        self.assertFalse(segment(resample(ordinary))[0][0]["forced"])
        self.assertTrue(segment(resample(jump))[0][0]["forced"])

    def test_glitch(self):
        self.assertEqual(len(clean([[0, 0, 0, 0], [.1, 20, 0, 0]])), 1)

    def test_short(self):
        self.assertEqual(segment([]), ([], []))
        self.assertEqual(segment([[0, 0, 0]]), ([], []))

    def test_same_timestamp_positions_are_averaged_before_resampling(self):
        samples = [[1.0, 0.0, 0.0, None], [1.0, 0.2, 0.0, None],
                   [1.0, 0.4, 0.0, None], [1.5, 1.0, 0.0, None]]
        collapsed = clean(samples)
        self.assertEqual(len(collapsed), 2)
        self.assertAlmostEqual(collapsed[0][1], 0.2)
        self.assertEqual(resample(collapsed)[-1][0], 1.5)

    def test_resample_skips_zero_length_interval_defensively(self):
        grid = resample([[1.0, 0.0, 0.0], [1.0, 0.2, 0.0], [1.5, 1.0, 0.0]])
        self.assertEqual(grid[-1][0], 1.5)

    def test_all_samples_at_one_timestamp_do_not_make_a_track(self):
        samples = [[1.0, 0.0, 0.0, None], [1.0, 0.2, 0.0, None]]
        self.assertEqual(segment(resample(clean(samples))), ([], []))
