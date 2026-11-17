import math
import unittest
from habitminer.metrics import profile_metrics


def move(path, t=0):
    return {"t0": t, "t1": t + 1, "p0": path[0][1:], "p1": path[-1][1:], "path": path,
            "length": 5, "cue": None, "deadline": None, "slack": None}


class MetricsTests(unittest.TestCase):
    def setUp(self):
        self.record = {"playerHits": {1: []}, "enemyCasts": [], "playerCasts": {1: []}, "samples": {1: []}}

    def profile(self, moves=None, rests=None, role="melee"):
        return profile_metrics(moves or [], rests or [], self.record, 1, role, {})

    def test_straight_bulge(self):
        path = [[0, 0, 0], [.25, 1, 0], [.5, 3, 0], [1, 5, 0]]
        self.assertEqual(self.profile([move(path)])["path"]["bulge"], [0])

    def test_offset_bulge(self):
        path = [[0, 0, 0], [.25, 1, 0], [.5, 2.5, 1], [1, 5, 0]]
        self.assertEqual(self.profile([move(path)])["path"]["bulge"], [.2])

    def test_pause(self):
        a = move([[0, 0, 0], [1, 2, 0]])
        b = move([[1.5, 2, 0], [2.5, 5, 0]], 1.5)
        rest = {"t0": 1, "t1": 1.5, "points": [[1, 2, 0], [1.5, 2, 0]]}
        p = self.profile([a, b], [rest])["path"]
        self.assertEqual(p["pauseChance"], 1)
        self.assertEqual(p["pause"], [.5])

    def test_overshoot(self):
        path = [[0, 0, 0], [.5, 3, 0], [.8, 5.5, 0], [1, 5, 0]]
        p = self.profile([move(path)])["path"]
        self.assertEqual(p["overshoot"], [.5])
        self.assertEqual(p["overshootChance"], 1)
        path[2][1] = 5.2
        self.assertEqual(self.profile([move(path)])["path"]["overshootChance"], 0)

    def test_switch_rate(self):
        for t, angle in [(0, 90), (30, 150), (60, 90)]:
            radians = math.radians(angle)
            self.record["playerHits"][1].append([t, "Boss", 0, 0, 0, math.cos(radians), math.sin(radians)])
        self.assertAlmostEqual(self.profile()["uptime"]["switchRate"], 2)

    def test_no_uptime(self):
        uptime = self.profile()["uptime"]
        self.assertEqual(uptime["angle"], [])
        self.assertEqual(uptime["centreDistance"], {})
        self.assertIsNone(uptime["switchRate"])

    def test_reaction_and_slack_use(self):
        short = move([[0, 0, 0], [1, 5, 0]], 2)
        short.update(cue={"t": 1, "type": "debuff"}, slack=1)
        long = move([[0, 0, 0], [1, 5, 0]], 5)
        long.update(cue={"t": 1, "type": "debuff"}, slack=4, deadline=10)
        data = profile_metrics([short, long], [], self.record, 1, "melee", {"debuff": 1})
        self.assertEqual(data["reaction"]["debuff"], [1])
        self.assertAlmostEqual(data["slackUse"][0], 3 / 6.2)

    def test_settle_and_idle(self):
        self_rests = {"actorId": 1, "t0": 0, "t1": 2, "points": [[0, 0, 0], [1, .5, 0], [2, .5, 0]]}
        others = [{"actorId": aid, "t0": 0, "t1": 2, "points": [[0, x, 0], [2, x, 0]]}
                  for aid, x in ((2, 1), (3, 2))]
        data = self.profile([], [self_rests, *others])
        self.assertEqual(len(data["settle"]), 1)
        self.assertEqual(data["idle"]["rate"], .5)
        self.assertEqual(data["idle"]["step"], [.5])

    def test_cast_still_and_uptime_cue_exclusion(self):
        self.record["samples"][1] = [[0, 0, 0, 0], [1, .1, 0, 0]]
        self.record["playerCasts"][1] = [[0, "begincast", 1], [1, "cast", 1]]
        self.record["playerHits"][1] = [[10, "Boss", 0, 0, 0, 1, 0]]
        self.record["enemyCasts"] = [[7, "begincast", 2]]
        data = self.profile(role="healer")
        self.assertEqual(data["uptime"]["castStill"], 1)
        self.assertEqual(data["uptime"]["angle"], [])
