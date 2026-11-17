import math


class Grid(list):
    def __init__(self, values, raw=None):
        super().__init__(values)
        self.raw = raw or []


def clean(samples):
    collapsed = []
    for sample in sorted(samples, key=lambda point: point[0]):
        if collapsed and sample[0] == collapsed[-1][0]:
            collapsed[-1][1].append(sample)
        else:
            collapsed.append([sample[0], [sample]])
    kept = []
    for timestamp, same_time in collapsed:
        sample = [timestamp, sum(point[1] for point in same_time) / len(same_time),
                  sum(point[2] for point in same_time) / len(same_time)]
        if len(same_time[0]) > 3:
            sample.append(next((point[3] for point in same_time if point[3] is not None), None))
        if kept and 0 < sample[0] - kept[-1][0] <= .25 and math.dist(sample[1:3], kept[-1][1:3]) > 15:
            continue
        kept.append(sample)
    return kept


def pieces(samples, max_gap=1.5):
    result = []
    for sample in samples:
        if not result or sample[0] - result[-1][-1][0] > max_gap:
            result.append([])
        result[-1].append(sample)
    return result


def resample(piece, step=.25):
    if len(piece) < 2:
        return Grid([], piece)
    result = []
    cursor = piece[0][0]
    i = 0
    while cursor < piece[-1][0] - 1e-9:
        while i + 1 < len(piece) - 1 and piece[i + 1][0] <= piece[i][0]:
            i += 1
        while i + 1 < len(piece) - 1 and piece[i + 1][0] < cursor:
            i += 1
        a, b = piece[i:i + 2]
        if b[0] <= a[0]:
            break
        weight = (cursor - a[0]) / (b[0] - a[0])
        result.append([round(cursor, 9), a[1] + weight * (b[1] - a[1]), a[2] + weight * (b[2] - a[2])])
        cursor += step
    result.append([piece[-1][0], piece[-1][1], piece[-1][2]])
    return Grid(result, piece)


def segment(grid, move_speed=1.5, min_move=.3, merge_gap=.25, min_rest=.5):
    if len(grid) < 2:
        return [], []
    states = [math.dist(a[1:3], b[1:3]) / (b[0] - a[0]) > move_speed if b[0] > a[0] else False
              for a, b in zip(grid, grid[1:])]
    spans = []
    start = 0
    for i in range(1, len(states) + 1):
        if i == len(states) or states[i] != states[start]:
            spans.append([states[start], start, i])
            start = i
    for i in range(1, len(spans) - 1):
        if not spans[i][0] and spans[i-1][0] and spans[i+1][0] and grid[spans[i][2]][0] - grid[spans[i][1]][0] <= merge_gap:
            spans[i][0] = True
    for i in range(len(spans) - 1):
        if spans[i][0] and not spans[i + 1][0] and spans[i][2] + 1 < len(grid):
            edge = spans[i][2]
            if math.dist(grid[edge][1:3], grid[edge + 1][1:3]) > 0:
                spans[i][2] += 1
                spans[i + 1][1] += 1
    moves, rests = [], []
    for moving, a, b in spans:
        path = grid[a:b + 1]
        duration = path[-1][0] - path[0][0]
        if moving and duration >= min_move:
            raw = getattr(grid, "raw", grid)
            forced = any(x[0] >= path[0][0] and y[0] <= path[-1][0] and y[0] - x[0] >= .2
                         and math.dist(x[1:3], y[1:3]) >= 2
                         and math.dist(x[1:3], y[1:3]) / (y[0] - x[0]) > 9 for x, y in zip(raw, raw[1:]))
            moves.append({"t0": path[0][0], "t1": path[-1][0], "p0": path[0][1:3], "p1": path[-1][1:3],
                          "path": path, "length": sum(math.dist(x[1:3], y[1:3]) for x, y in zip(path, path[1:])), "forced": forced})
        elif not moving and duration >= min_rest:
            rests.append({"t0": path[0][0], "t1": path[-1][0], "points": path})
    return moves, rests
