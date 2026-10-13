#if DEBUG
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Haugbui.Spikes
{
    internal static class SpikeUtils
    {
        public static string FilePath(string name) => Path.Combine(BepInEx.Paths.BepInExRootPath, name);

        public static List<Container> PlayerBuiltContainersNear(Vector3 center, float radius)
        {
            return Physics.OverlapSphere(center, radius)
                .Select(c => c.GetComponentInParent<Container>())
                .Where(c => c != null && c.GetComponent<Piece>() != null && c.m_nview != null && c.m_nview.IsValid())
                .Distinct()
                .OrderBy(c => Vector3.Distance(center, c.transform.position))
                .ToList();
        }

        public static T NearestComponent<T>(Vector3 center, float radius) where T : Component
        {
            return Physics.OverlapSphere(center, radius)
                .Select(c => c.GetComponentInParent<T>())
                .Where(c => c != null)
                .OrderBy(c => Vector3.Distance(center, c.transform.position))
                .FirstOrDefault();
        }

        public static Dictionary<string, int> Totals(IEnumerable<Inventory> inventories)
        {
            var totals = new Dictionary<string, int>();
            foreach (ItemDrop.ItemData item in inventories.SelectMany(i => i.GetAllItems()))
            {
                string key = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                totals.TryGetValue(key, out int current);
                totals[key] = current + item.m_stack;
            }
            return totals;
        }

        public static string Format(Dictionary<string, int> totals) =>
            string.Join(";", totals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));

        public static string Format(ZDOID id) => $"{id.UserID}:{id.ID}";

        public static bool TryParseZdoId(string text, out ZDOID id)
        {
            id = ZDOID.None;
            string[] parts = text.Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], out long user) || !uint.TryParse(parts[1], out uint value))
                return false;
            id = new ZDOID(user, value);
            return true;
        }
    }
}
#endif
