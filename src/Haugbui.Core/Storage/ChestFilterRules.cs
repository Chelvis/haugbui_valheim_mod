using System;
using System.Collections.Generic;
using Haugbui.Core.Persistence;

namespace Haugbui.Core.Storage
{
    /// <summary>
    /// HB-062 chest filter. An empty list keeps the existing-item rule. A non-empty list accepts only those prefabs,
    /// even into an empty chest, and refuses everything else. A stack cap limits how many stacks of that prefab the
    /// chest may hold. Stacking and item data are unchanged, and items already over the cap stay.
    /// </summary>
    public static class ChestFilterRules
    {
        /// <summary>Cap on the saved prefab list. Not the chest's slot count: a full chest still refuses what does not fit.</summary>
        public const int MaxEntries = 10;

        /// <summary>Largest stack cap a row can store. Above this, the field clamps. 0 on a line means no cap.</summary>
        public const int MaxStackLimit = 999;

        public static bool IsSet(IReadOnlyList<string> filter) => filter != null && filter.Count > 0;

        public static bool Allows(IReadOnlyList<string> filter, string prefab)
        {
            if (!IsSet(filter)) return true;
            if (string.IsNullOrEmpty(prefab)) return false;
            for (int i = 0; i < filter.Count; i++)
                if (string.Equals(filter[i], prefab, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Trims, drops blanks and duplicates, keeps the first occurrence, and stops at <see cref="MaxEntries"/>.</summary>
        public static List<string> Normalize(IEnumerable<string> prefabs)
        {
            var result = new List<string>();
            if (prefabs == null) return result;
            foreach (string raw in prefabs)
            {
                if (result.Count >= MaxEntries) break;
                string name = raw?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (result.Exists(existing => string.Equals(existing, name, StringComparison.Ordinal))) continue;
                result.Add(name);
            }
            return result;
        }

        /// <summary>Same trimming as the name list. A non-positive cap is stored as 0, which means no cap.</summary>
        public static List<ChestFilterLine> Normalize(IEnumerable<ChestFilterLine> lines)
        {
            var result = new List<ChestFilterLine>();
            if (lines == null) return result;
            foreach (ChestFilterLine line in lines)
            {
                if (result.Count >= MaxEntries) break;
                string name = line?.Prefab?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (result.Exists(existing => string.Equals(existing.Prefab, name, StringComparison.Ordinal))) continue;
                int stacks = line.MaxStacks;
                if (stacks < 1) stacks = 0;
                else if (stacks > MaxStackLimit) stacks = MaxStackLimit;
                result.Add(new ChestFilterLine(name, stacks));
            }
            return result;
        }

        /// <summary>
        /// Room left under a stack cap. <paramref name="maxStacks"/> of 0 is no cap.
        /// Already over the cap accepts nothing. Sitting on the cap can still fill the open stacks.
        /// </summary>
        public static int CappedRoom(int stacksNow, int stackRoom, int freeSlots, int maxStack, int maxStacks)
        {
            int slot = Math.Max(1, maxStack);
            int chest = Math.Max(0, stackRoom) + Math.Max(0, freeSlots) * slot;
            if (maxStacks <= 0) return chest;
            if (stacksNow > maxStacks) return 0;
            int open = maxStacks - Math.Max(0, stacksNow);
            return Math.Min(chest, Math.Max(0, stackRoom) + open * slot);
        }
    }
}
