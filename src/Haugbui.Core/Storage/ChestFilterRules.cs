using System;
using System.Collections.Generic;

namespace Haugbui.Core.Storage
{
    /// <summary>
    /// HB-062 chest filter. An empty list keeps the existing-item rule. A non-empty list accepts only those prefabs,
    /// even into an empty chest, and refuses everything else. Stacking and item data are unchanged.
    /// </summary>
    public static class ChestFilterRules
    {
        /// <summary>Cap on the saved prefab list. Not the chest's slot count: a full chest still refuses what does not fit.</summary>
        public const int MaxEntries = 10;

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
    }
}
