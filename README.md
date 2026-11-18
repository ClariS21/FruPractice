# ![FRU Practice](images/icon.png) FRU Practice

A private Dalamud plugin for practising Futures Rewritten (Ultimate) and Dancing Mad (Ultimate) phases 3 to 5 solo, client-side, with bots.

It is a fork of [shifusen329/AnoMech](https://github.com/shifusen329/AnoMech), which is itself a fork of
[anomek/AnoMech](https://github.com/anomek/AnoMech). It spawns fake party members and bosses into your game client in an Inn and plays the
mechanics out with real VFX, cast bars and tethers. Licensed under AGPL-3.0 (see `LICENSE.md`); the AnoMech authors' credit is kept below.

**WARNING:** you are cut off from server traffic while in the sim zone. The plugin firewalls incoming packets to keep the fake zone stable, so
party changes will not show and ready checks will not pop until you leave. AnoMech's sim and FRU Practice's sim must not run at the same time.

## Install (Dalamud dev plugin)

1. Build: `dotnet build --configuration Release` from the repo root (the Dalamud SDK resolves from `%AppData%/XIVLauncher/addon/Hooks/dev/`).
2. Copy the folder holding `FruPractice.dll` and `FruPractice.json` (`AnoMech/bin/x64/Release/`) to the PC that runs the game. Use a new folder for each test build.
3. In `/xlsettings` -> Experimental -> Dev Plugin Locations, add that folder, then enable FRU Practice in `/xlplugins`.
4. Go to an Inn and open the plugin with `/frup`.

## Commands

- `/frup`: open the main window.
- `/frup config`: settings.
- `/frup capture [name]`: add your character's look to your collection of looks for the Hana main-tank bot (identical looks are not added twice).
- `/frup looks`: list the shipped and captured looks. `/frup looks remove <n>`: delete captured look `n`.
- `/frup start` (or `start solo`): start the selected scenario. `/frup reset`: restart it. `/frup leave`: leave the sim.

## What it practises

### Futures Rewritten (Ultimate)

- P3: Ultimate Relativity, Apocalypse, and the full phase.
- P4: Darklit Dragonsong (both strats: "NA" and "NA, you take Somber Dance"), Crystallize Time, and the full phase.
- P5: Fulgent Blade, Paradise Regained (with Polarizing Strikes), and the full phase.

### Dancing Mad (Ultimate)

- P3: Limit Cut, Black Hole, and the full phase.
- P4: Kefka Says, and the full phase.
- P5: Celestriad, Exaflares, Flood, Forsaken Null, and the full phase.
- The tanks follow the Omnitank priority for their pair (Warrior, then Dark Knight, Gunbreaker, Paladin): it decides who holds Chaos in P3 and who invulns the first Surprise Holy in P5.
- The sim has no boss HP. The P4 full phase's HP check is a Pass/Fail setting, and in the P5 full phase Kefka dies at a time you pick (214 s by default).

Each drill runs on its own; the full-phase runs chain a phase's mechanics, carrying their results from one into the next.

## Bots

- Human-like reaction timing, or "last moment" timing.
- Every bot holds its job's Ultimate Edenmorn weapon, drawn, so animations match the job.
- Job rotations with cast bars, gap closers and burst windows.
- Mitigation sheet practice: bots press their own mitigation to the plan.
- Rolled HP and shields per job.
- The Hana Shirosaki main-tank bot wears a look drawn at random each run from the shipped looks plus the ones you capture (never the same twice in a row). A tank look makes her play that job; a non-tank look plays the default tank.

## ActBridge (ACT plugin for cactbot)

`ActBridge/` is a small ACT plugin that receives the sim's log lines over loopback so overlays such as cactbot can read them. See [ActBridge/README.md](ActBridge/README.md).

## Known issues

- Visuals closely match retail, but damage snapshot timings are best guesses.
- Not all skills resolve properly.
- Dancing Mad (Ultimate) has no mitigation plans, guide danger zones or uptime windows yet, and its runs send nothing to ACT, so cactbot stays quiet there.

## Acknowledgments

Thanks to the contributors of AnoMech:
* [WorstAquaPlayer](https://github.com/WorstAquaPlayer) - rewriting core & fixing crashes, extra scenarios
* [Wydox](https://github.com/Wydox) - core improvements, extra scenarios and strategies
* [Evernow](https://github.com/Evernow) - multiplayer, core improvements, extra scenarios and strategies
* [RoarkGit](https://github.com/RoarkGit) - win streaks, extra scenarios
* [grayparser](https://github.com/grayparser) - core improvements, extra scenarios (including the FRU scenarios)
* [altair9x](https://github.com/altair9x) - extra strategies
* [Quantumlyy](https://github.com/Quantumlyy) - extra strategies
* [Linqx](https://github.com/Linqx) - success/failure checkmarks

The zone changes are built on **Hyperborea**, which served as the baseline for loading a solo duty arena
([Evernow](https://github.com/Evernow) has since improved that code). **FFXIV-RaidsRewritten** and **bossmod** were the initial inspiration for AnoMech.
