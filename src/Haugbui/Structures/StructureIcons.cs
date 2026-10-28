using System.Collections.Generic;
using System.IO;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>
    /// Build-menu icons from icons/*.png next to the plugin. A missing or unreadable file keeps the vanilla item icon.
    /// </summary>
    internal static class StructureIcons
    {
        private static readonly Dictionary<StructureKind, string> Used = new Dictionary<StructureKind, string>();

        public static Sprite Load(StructureDefinition definition)
        {
            Sprite custom = TryLoad(definition);
            if (custom != null)
            {
                Used[definition.Kind] = "custom";
                return custom;
            }
            Used[definition.Kind] = "fallback";
            return ItemIcon(definition.IconItem);
        }

        /// <summary>"custom" or "fallback", for the self-test. "missing" if the piece was never registered.</summary>
        public static string Describe(StructureKind kind) =>
            Used.TryGetValue(kind, out string source) ? source : "missing";

        private static Sprite TryLoad(StructureDefinition definition)
        {
            if (string.IsNullOrEmpty(definition.IconFile)) return null;
            string directory = Path.GetDirectoryName(typeof(StructureIcons).Assembly.Location);
            if (string.IsNullOrEmpty(directory)) return null;
            string path = Path.Combine(directory, "icons", definition.IconFile);
            if (!File.Exists(path))
            {
                Log.Warning($"Icon {definition.IconFile} is missing; {definition.PieceId} keeps the item icon.");
                return null;
            }
            Sprite sprite = AssetUtils.LoadSpriteFromFile(path);
            if (sprite == null) Log.Warning($"Icon {definition.IconFile} could not be loaded; {definition.PieceId} keeps the item icon.");
            return sprite;
        }

        private static Sprite ItemIcon(string item)
        {
            ItemDrop drop = PrefabManager.Cache.GetPrefab<ItemDrop>(item);
            Sprite[] icons = drop != null ? drop.m_itemData.m_shared.m_icons : null;
            return icons != null && icons.Length > 0 ? icons[0] : null;
        }
    }
}
