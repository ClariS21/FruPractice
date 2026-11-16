namespace AnoMech.Core.Native.Interfaces;

// ModelMain and ModelSub are the packed weapon models: set in bits 0-15, base in 16-31, variant in 32-47.
public sealed record ItemRow(uint Id, string Name, uint LevelItem, uint ClassJobCategory, ulong ModelMain, ulong ModelSub);
