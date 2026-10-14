using System;
using System.Collections.Generic;
using System.Text;

namespace Haugbui.Core.Persistence
{
    /// <summary>HB-137: one servant of one player in one world. Outlives its tomb; never deleted.</summary>
    public sealed class ServantSlot
    {
        public int Index { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>The tomb bound to this slot; empty while vacant.</summary>
        public string TombGuid { get; set; } = string.Empty;

        /// <summary>World time of a death whose return is still pending; NaN otherwise. Inherited by a new tomb.</summary>
        public double DeathTime { get; set; } = double.NaN;

        public TombToggles Toggles { get; set; } = TombToggles.Defaults;

        public bool IsVacant => string.IsNullOrEmpty(TombGuid);
    }

    public sealed class ServantSlotSerializer : IBlobSerializer<ServantSlot>
    {
        public static readonly ServantSlotSerializer Instance = new ServantSlotSerializer();

        public int CurrentVersion => 1;

        public void Write(ServantSlot value, BlobWriter writer)
        {
            writer.Write(value.Index).Write(value.Name).Write(value.TombGuid).Write(value.DeathTime).Write((int)value.Toggles);
        }

        public ServantSlot Read(BlobReader reader) => new ServantSlot
        {
            Index = reader.ReadInt(),
            Name = reader.ReadString(),
            TombGuid = reader.ReadString(),
            DeathTime = reader.ReadDouble(),
            Toggles = (TombToggles)reader.ReadInt(),
        };
    }

    public static class ServantSlots
    {
        /// <summary>HB-139: "Haugbui", "Haugbui #2", "Haugbui #3"…</summary>
        public static string DefaultName(int index) => index == 0 ? "Haugbui" : $"Haugbui #{index + 1}";

        /// <summary>
        /// HB-138: the lowest slot below <paramref name="max"/> whose tomb no longer exists. A slot naming a tomb that is
        /// gone (e.g. removed while the mod was missing) counts as vacant. Returns -1 when every slot is bound.
        /// </summary>
        public static int LowestVacant(IReadOnlyDictionary<int, ServantSlot> slots, int max, Func<string, bool> tombExists)
        {
            for (int i = 0; i < max; i++)
            {
                if (!slots.TryGetValue(i, out ServantSlot slot) || slot == null || slot.IsVacant || !tombExists(slot.TombGuid)) return i;
            }
            return -1;
        }

        /// <summary>Global key name; ZoneSystem lower-cases names and splits the value at the first space.</summary>
        public static string KeyName(long ownerId, int index) => $"haugbui_slot_{ownerId}_{index}";

        public static string KeyPrefix(long ownerId) => $"haugbui_slot_{ownerId}_";

        public static bool TryParseIndex(string keyName, long ownerId, out int index)
        {
            index = -1;
            string prefix = KeyPrefix(ownerId);
            return keyName.StartsWith(prefix, StringComparison.Ordinal)
                   && int.TryParse(keyName.Substring(prefix.Length), out index) && index >= 0;
        }

        /// <summary>
        /// Lower-case hex, because ZoneSystem.GlobalKeyAdd lower-cases the whole key text when the world loads (1.0.17).
        /// </summary>
        public static string Encode(ServantSlot slot)
        {
            byte[] bytes = ServantSlotSerializer.Instance.Serialize(slot);
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static BlobReadStatus Decode(string text, out ServantSlot slot)
        {
            slot = null;
            if (string.IsNullOrEmpty(text)) return BlobReadStatus.Missing;
            if (text.Length % 2 != 0) return BlobReadStatus.Corrupt;
            var bytes = new byte[text.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = HexValue(text[2 * i]), lo = HexValue(text[2 * i + 1]);
                if (hi < 0 || lo < 0) return BlobReadStatus.Corrupt;
                bytes[i] = (byte)(hi * 16 + lo);
            }
            return ServantSlotSerializer.Instance.TryDeserialize(bytes, out slot);
        }

        private static int HexValue(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
