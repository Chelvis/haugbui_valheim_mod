using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Diagnostics;

namespace Haugbui.Integration
{
    /// <summary>
    /// World marker keys, written straight into m_globalKeys / m_globalKeysValues.
    /// Verified on 1.0.20 ZoneSystem: SetGlobalKey still routes an RPC, and GlobalKeyAdd lower-cases the whole line
    /// and copies it into the player profile. Loading does the same lower-casing, so the values stay plain numbers.
    /// </summary>
    internal static class WorldMarker
    {
        public static bool Available => ZoneSystem.instance != null;

        public static MarkerReading Read()
        {
            if (!Available) return new MarkerReading(false, 0, false, 0);
            Dictionary<string, string> values = ZoneSystem.instance.m_globalKeysValues;
            values.TryGetValue(WorldGuard.MarkerKey, out string schema);
            values.TryGetValue(WorldGuard.TombCountKey, out string count);
            return WorldGuard.Read(schema, count);
        }

        public static bool HasServantSlots()
        {
            if (!Available) return false;
            foreach (string key in ZoneSystem.instance.m_globalKeysValues.Keys)
                if (key.StartsWith("haugbui_slot_", System.StringComparison.Ordinal)) return true;
            return false;
        }

        public static void Write(int tombCount)
        {
            if (!Available || tombCount < 0) return;
            Set(WorldGuard.MarkerKey, WorldGuard.Schema.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Set(WorldGuard.TombCountKey, tombCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static void Set(string name, string value)
        {
            ZoneSystem zones = ZoneSystem.instance;
            if (zones.m_globalKeysValues.TryGetValue(name, out string old))
            {
                if (old == value) return;
                zones.m_globalKeys.Remove((name + " " + old).TrimEnd());
            }
            zones.m_globalKeys.Add(name + " " + value);
            zones.m_globalKeysValues[name] = value;
        }
    }
}
