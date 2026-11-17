def attach(move: dict, player_applied: list, enemy_casts: list, damage_taken: list | None = None) -> dict:
    result = dict(move)
    t0, t1 = move["t0"], move["t1"]
    damage_taken = damage_taken or []
    candidates = [{"t": row[0], "type": "debuff"} for row in player_applied
                  if row[2] == "debuff" and row[3] and t0 - 6 <= row[0] <= t0 - .05]
    candidates.extend({"t": row[0], "type": "cast"} for row in enemy_casts
                      if row[1] == "begincast" and t0 - 6 <= row[0] <= t0 - .05)
    candidates.extend({"t": row[0], "type": "resolve"} for row in enemy_casts
                      if row[1] == "cast" and t0 - 6 <= row[0] <= t0 - .05
                      and any(hit[1] == row[2] and row[0] <= hit[0] <= row[0] + 1.5 for hit in damage_taken))
    cue = max(candidates, key=lambda x: x["t"], default=None)
    deadlines = [row[0] for row in enemy_casts if row[1] == "cast" and t1 < row[0] <= t1 + 8]
    deadline = min(deadlines, default=None)
    result.update(cue=cue, deadline=deadline,
                  slack=deadline - cue["t"] - (t1 - t0) - .8 if cue and deadline is not None else None)
    return result
