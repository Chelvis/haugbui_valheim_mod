using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;

namespace Haugbui.UI
{
    /// <summary>
    /// One filter list and one delivery list, kept until the game closes. A later copy replaces the previous one.
    /// </summary>
    internal static class PanelClipboard
    {
        private static List<ChestFilterLine> _filters;
        private static List<(string Prefab, int Requested)> _requests;

        internal static bool HasFilters { get; private set; }
        internal static bool HasRequests { get; private set; }

        internal static void Reset()
        {
            _filters = null;
            _requests = null;
            HasFilters = false;
            HasRequests = false;
        }

        internal static void CopyFilters(IEnumerable<ChestFilterLine> lines)
        {
            _filters = ChestFilterRules.Normalize(lines);
            HasFilters = true;
        }

        internal static List<ChestFilterLine> FilterLines()
        {
            var copy = new List<ChestFilterLine>();
            if (_filters == null) return copy;
            foreach (ChestFilterLine line in _filters)
                copy.Add(new ChestFilterLine(line.Prefab, line.MaxStacks));
            return copy;
        }

        internal static void CopyRequests(IEnumerable<(string Prefab, int Requested)> lines)
        {
            _requests = new List<(string Prefab, int Requested)>();
            if (lines != null)
                foreach ((string prefab, int requested) in lines)
                    _requests.Add((prefab, requested));
            HasRequests = true;
        }

        internal static IReadOnlyList<(string Prefab, int Requested)> Requests =>
            _requests ?? (IReadOnlyList<(string Prefab, int Requested)>)System.Array.Empty<(string, int)>();
    }
}
