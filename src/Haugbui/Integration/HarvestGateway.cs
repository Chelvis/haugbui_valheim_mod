using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Harvest;
using Haugbui.Creature;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>
    /// HB-T-055/056: crops, beehives, birds' nests and sap collectors through their own vanilla RPCs, so drop tables,
    /// effects and respawn rules stay the game's. An RPC to the local owner runs at once (ZRoutedRpc.InvokeRoutedRPC),
    /// so the drops exist when <see cref="Harvest"/> returns.
    /// </summary>
    internal static class HarvestGateway
    {
        /// <summary>Drops spawn at the target's spawn point with a small random offset (Pickable.Drop, Beehive.RPC_Extract).</summary>
        private const float DropSearchRadius = 4f;

        private static readonly HashSet<ItemDrop> Before = new HashSet<ItemDrop>();

        public static ZNetView ViewOf(MonoBehaviour target)
        {
            switch (target)
            {
                case Pickable pickable: return pickable.m_nview;
                case Beehive hive: return hive.m_nview;
                case SapCollector sap: return sap.m_nview;
                default: return null;
            }
        }

        public static string IdOf(MonoBehaviour target)
        {
            ZDO zdo = ViewOf(target)?.GetZDO();
            return zdo != null ? zdo.m_uid.ToString() : "harvest:none";
        }

        public static HarvestTargetKind KindOf(MonoBehaviour target) =>
            target is Beehive ? HarvestTargetKind.Beehive : target is SapCollector ? HarvestTargetKind.SapCollector : HarvestTargetKind.Pickable;

        public static bool IsReady(MonoBehaviour target)
        {
            ZNetView view = ViewOf(target);
            if (view == null || !view.IsValid()) return false;
            switch (target)
            {
                case Pickable pickable: return !pickable.GetPicked() && pickable.CanBePicked();
                case Beehive hive: return hive.GetHoneyLevel() >= 1;
                case SapCollector sap: return sap.GetLevel() >= 1;
                default: return false;
            }
        }

        /// <summary>The main product with the amount one harvest yields (extra drops are not counted).</summary>
        public static ItemStack ExpectedProduct(MonoBehaviour target)
        {
            GameObject prefab;
            int amount;
            int worldLevel = 0;
            switch (target)
            {
                case Pickable pickable:
                    prefab = pickable.m_itemPrefab;
                    amount = Mathf.Max(1, pickable.m_amount);
                    // Pickable.Drop calls ItemDrop.OnCreateNew, which stamps the world level.
                    worldLevel = Game.m_worldLevel;
                    break;
                case Beehive hive:
                    prefab = hive.m_honeyItem != null ? hive.m_honeyItem.gameObject : null;
                    amount = Mathf.Max(1, hive.GetHoneyLevel());
                    break;
                case SapCollector sap:
                    prefab = sap.m_spawnItem != null ? sap.m_spawnItem.gameObject : null;
                    amount = Mathf.Max(1, sap.GetLevel());
                    break;
                default:
                    return null;
            }
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) return null;
            ItemStack stack = ItemBridge.FromItemData(drop.m_itemData);
            stack.Prefab = Utils.GetPrefabName(prefab);
            stack.Amount = amount;
            stack.WorldLevel = worldLevel;
            stack.Cheated = false;
            return stack;
        }

        public static HarvestFacts Facts(MonoBehaviour target, ItemStack product, bool wardAllows) => new HarvestFacts
        {
            Kind = KindOf(target),
            Cultivated = target is Pickable p && p.m_harvestable,
            Ready = IsReady(target),
            Aggravates = target is Pickable a && a.m_aggravateRange > 0f,
            ProductTransportable = product != null && ItemBridge.IsTransportable(product),
            WardAllows = wardAllows,
        };

        /// <summary>Runs the target's own action and returns the drops it spawned.</summary>
        public static List<ItemDrop> Harvest(MonoBehaviour target)
        {
            var spawned = new List<ItemDrop>();
            ZNetView view = ViewOf(target);
            if (view == null || !view.IsValid() || !IsReady(target)) return spawned;

            Vector3 origin = target.transform.position;
            Before.Clear();
            foreach (ItemDrop drop in ItemDrop.s_instances) Before.Add(drop);
            switch (target)
            {
                case Pickable _:
                    view.InvokeRPC("RPC_Pick", 0);
                    break;
                default:
                    view.InvokeRPC("RPC_Extract");
                    break;
            }
            foreach (ItemDrop drop in ItemDrop.s_instances)
            {
                if (drop == null || Before.Contains(drop)) continue;
                if (Vector3.Distance(drop.transform.position, origin) <= DropSearchRadius) spawned.Add(drop);
            }
            Before.Clear();
            return spawned;
        }
    }
}
