using System;
using AnoMech.Core.EnemyActions;
using AnoMech.Core.Game.Party;
using System.Collections.Generic;
using AnoMech.Core.UserActions;

namespace AnoMech.Core.Game.PartyMit;

// A mitigation sheet's columns. The healer columns are each one job, wherever it sits; Extras is
// whichever slot holds one of the entry's Jobs.
public enum SheetColumn { MT, OT, WHM, AST, SCH, SGE, D1, D2, D3, D4, Extras }

// One sheet row: who presses what, when (scenario seconds; negative is before the scenario starts,
// applied at its start and never graded), and the hits it must still be up for. Jobs narrows the
// row to those jobs; On is the boss a debuff goes on, when not the plan's DebuffTarget; Buddy aims
// a single-target button (Intervention, Nascent Flash) at the other tank; ForBaiter keeps the row
// to the one slot the plan's Baiter names, once the run knows it; Partners keeps a tank row to
// comps where the other tank's job is one of them (the sheet's tank-pair tabs).
public sealed record MitPlanEntry(SheetColumn Column, uint ActionId, float At, string Label, float[] Covers, JobId[]? Jobs = null,
    MitSource? On = null, bool Buddy = false, bool ForBaiter = false, JobId[]? Partners = null)
{
    // Placeholder buttons, resolved per job by MitCatalogue.For.
    public const uint PartyMit = 0;
    public const uint TankLb3 = 1;

    public bool IsExtra => Column == SheetColumn.Extras;
    public bool IsPreStart => At < 0f;
}

// Who a hit comes from, which decides whether the party's debuffs on the boss reduce it.
public sealed record MitSource(string Name);

// Raw is the unmitigated damage on a physical DPS, or null while unknown (the hit moves no HP).
// ActionIds are the EnemyActions that deal it (one per variant), landing it on whoever they resolve on;
// empty for a hit the scenario lands itself with MitPractice.Hit.
// A tank buster ignores the class factor; Fresh lands on full HP whatever came before; ToOneHp
// leaves whoever it catches on 1 HP (Hell's Judgment) instead of dealing damage; TankRaw is what a
// tank takes when that isn't its class share of Raw; TargetOnly leaves everyone but the action's
// target out of it (a shared tank buster the sheet gives its second tank no cooldowns for);
// SoloRaw is what the one it caught takes when it is alone (an understacked share).
public sealed record MitHitDef(string Key, string Name, float? Raw, MitSource? Source, DamageKind Kind = DamageKind.Magic,
    bool TankBuster = false, bool Fresh = false, IReadOnlyList<uint>? ActionIds = null, bool ToOneHp = false, float? TankRaw = null,
    bool TargetOnly = false, float? SoloRaw = null);

public readonly record struct MitScheduledHit(float At, string[] Keys);

// DebuffTarget is the boss the party's Reprisal/Feint/Addle go on; null puts them on every boss.
public sealed record MitPlanData(MitProfile Profile, IReadOnlyList<MitHitDef> Hits, IReadOnlyList<MitScheduledHit> Schedule,
    IReadOnlyList<MitPlanEntry> Entries, MitSource? DebuffTarget = null, Func<PartyRole?>? Baiter = null);
