# FRU movement habit miner

This standalone Python tool reads FFLogs clears, measures anonymised player movement habits, and writes data for FRU Practice. It uses only the Python 3 standard library. The plugin does not run this script.

From the repository root:

```text
python -I tools/habit-miner/mine.py --creds <path-to-FFLogs-credentials>
```

Options: `--speed 40`, `--decent 20`, `--cache .tmp/fflogs-cache`, `--out-json AnoMech/Resources/movement/habits.json`, and `--out-summary docs/movement-habits-summary.md`. Run `python -I tools/habit-miner/mine.py --help` for the full argument list.

The credentials file path is required. It must contain a client UUID and a 40-character alphanumeric secret. The script reads it only when an uncached API request needs a token. Credentials and access tokens are never written to output.

Responses are cached under `.tmp/fflogs-cache/`, so interrupted runs can resume without downloading completed queries again. **Never commit anything under `.tmp/`.** The public output is `AnoMech/Resources/movement/habits.json`. The private review summary is `docs/movement-habits-summary.md`. Both outputs are replaced atomically after the mining work finishes.

Unit tests run without network access:

```text
python -I -m unittest discover -s tools/habit-miner/tests -t tools/habit-miner
```
