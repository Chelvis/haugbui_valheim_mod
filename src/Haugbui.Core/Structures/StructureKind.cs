using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Progression;

namespace Haugbui.Core.Structures
{
    /// <summary>Persisted as int in the haugbui_kind key; never renumber.</summary>
    public enum StructureKind
    {
        None = 0,
        Tomb = 1,
        Offering = 10,
        PersonalTreasure = 11,
        SilverBracelets = 12,
        Longsword = 13,
        Altar = 14,
        CollectionChest = 20,
        DeliveryChest = 21,
    }

    [Flags]
    public enum TombAbilities
    {
        None = 0,
        Combat = 1 << 0,
        FullTime = 1 << 1,
    }

    public readonly struct TombProgress
    {
        public TombProgress(int level, TombAbilities abilities)
        {
            Level = level;
            Abilities = abilities;
        }

        public int Level { get; }
        public TombAbilities Abilities { get; }

        public bool Has(TombAbilities ability) => (Abilities & ability) == ability;
    }

    public static class TombProgression
    {
        public static readonly IReadOnlyList<StructureKind> Upgrades = new[]
        {
            StructureKind.Offering, StructureKind.PersonalTreasure, StructureKind.SilverBracelets,
            StructureKind.Longsword, StructureKind.Altar
        };

        public static bool IsUpgrade(StructureKind kind) => Upgrades.Contains(kind);

        /// <summary>Level = 1 + distinct upgrade kinds, in any order; abilities come from specific upgrades only.</summary>
        public static TombProgress Compute(IEnumerable<StructureKind> boundUpgrades)
        {
            var distinct = new HashSet<StructureKind>(boundUpgrades.Where(IsUpgrade));
            var abilities = TombAbilities.None;
            if (distinct.Contains(StructureKind.Longsword)) abilities |= TombAbilities.Combat;
            if (distinct.Contains(StructureKind.Altar)) abilities |= TombAbilities.FullTime;
            int level = Math.Min(ProgressionTable.MinLevel + distinct.Count, ProgressionTable.MaxLevel);
            return new TombProgress(level, abilities);
        }
    }
}
