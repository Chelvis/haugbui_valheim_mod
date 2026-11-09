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
        private static List<RequestDraft> _requests;

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

        internal static void CopyRequests(IEnumerable<RequestDraft> lines)
        {
            _requests = new List<RequestDraft>();
            if (lines != null)
                foreach (RequestDraft line in lines)
                    _requests.Add(line);
            HasRequests = true;
        }

        internal static IReadOnlyList<RequestDraft> Requests =>
            _requests ?? (IReadOnlyList<RequestDraft>)System.Array.Empty<RequestDraft>();
    }
}
