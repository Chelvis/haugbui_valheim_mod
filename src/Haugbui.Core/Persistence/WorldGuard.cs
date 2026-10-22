using System.Globalization;

namespace Haugbui.Core.Persistence
{
    public enum WorldGuardVerdict
    {
        /// <summary>No marker and nothing of ours in the world. Do not write, do not warn.</summary>
        Clean,

        /// <summary>Haugbui data is here and the marker is missing or its count cannot be read. Record the scan, do not warn.</summary>
        Baseline,

        /// <summary>The marker matches the scan, or the scan found at least as many tombs as it remembers.</summary>
        Intact,

        /// <summary>The marker remembers more tombs than the scan found. Keep the remembered count so a restore can clear the warning.</summary>
        Missing
    }

    public readonly struct MarkerReading
    {
        public MarkerReading(bool present, int schema, bool countReadable, int recordedTombs)
        {
            Present = present;
            Schema = schema;
            CountReadable = countReadable;
            RecordedTombs = recordedTombs;
        }

        public bool Present { get; }
        public int Schema { get; }
        public bool CountReadable { get; }
        public int RecordedTombs { get; }

        /// <summary>A newer mod wrote this marker. Leave both keys alone.</summary>
        public bool FutureSchema => Present && Schema > WorldGuard.Schema;
    }

    /// <summary>
    /// World marker <c>haugbui &lt;schema&gt;</c> and <c>haugbui_tombs &lt;count&gt;</c>. The count is tomb ZDOs, not servant slots.
    /// Schema 1 is the world marker, not the tomb blob.
    /// </summary>
    public static class WorldGuard
    {
        public const int Schema = 1;
        public const string MarkerKey = "haugbui";
        public const string TombCountKey = "haugbui_tombs";

        public static bool HasData(bool markerPresent, int tombZdos, bool servantSlots) =>
            markerPresent || tombZdos > 0 || servantSlots;

        public static MarkerReading Read(string schemaText, string countText)
        {
            if (schemaText == null) return new MarkerReading(false, 0, false, 0);
            int schema = int.TryParse(schemaText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                ? parsed : 0;
            bool countReadable = int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) && count >= 0;
            return new MarkerReading(true, schema, countReadable, countReadable ? count : 0);
        }

        public static WorldGuardVerdict Compare(MarkerReading marker, int foundTombs, bool servantSlots)
        {
            if (foundTombs < 0) foundTombs = 0;
            if (!marker.Present)
                return HasData(false, foundTombs, servantSlots) ? WorldGuardVerdict.Baseline : WorldGuardVerdict.Clean;
            if (!marker.CountReadable) return marker.FutureSchema ? WorldGuardVerdict.Intact : WorldGuardVerdict.Baseline;
            return foundTombs < marker.RecordedTombs ? WorldGuardVerdict.Missing : WorldGuardVerdict.Intact;
        }

        /// <summary>False leaves the stored keys as they are (a shortfall, a future schema, or a clean world).</summary>
        public static bool ShouldWrite(MarkerReading marker, WorldGuardVerdict verdict, int foundTombs)
        {
            if (marker.FutureSchema || verdict == WorldGuardVerdict.Clean || verdict == WorldGuardVerdict.Missing) return false;
            if (verdict == WorldGuardVerdict.Intact
                && marker.Schema == Schema && marker.CountReadable && marker.RecordedTombs == foundTombs)
                return false;
            return true;
        }
    }
}
