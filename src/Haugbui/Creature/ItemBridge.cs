using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;
using UnityEngine;

namespace Haugbui.Creature
{
    /// <summary>Converts between Core item stacks and Valheim items.</summary>
    internal static class ItemBridge
    {
        public static ItemStack FromItemData(ItemDrop.ItemData item) => new ItemStack
        {
            Prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty,
            Amount = item.m_stack,
            Quality = item.m_quality,
            Variant = item.m_variant,
            WorldLevel = item.m_worldLevel,
            Cheated = item.m_cheated,
            CrafterId = item.m_crafterID,
            CrafterName = item.m_crafterName ?? string.Empty,
            CustomData = item.m_customData != null ? new Dictionary<string, string>(item.m_customData) : new Dictionary<string, string>(),
            UnitWeight = item.m_shared.m_weight,
            MaxStack = item.m_shared.m_maxStackSize,
        };

        public static string PrefabName(ItemDrop.ItemData item) => item.m_dropPrefab != null ? item.m_dropPrefab.name : string.Empty;

        /// <summary>The game's stacking rule (Inventory.FindFreeStackItem) against a Core stack.</summary>
        public static bool SameKind(ItemDrop.ItemData item, ItemStack stack) =>
            PrefabName(item) == stack.Prefab && item.m_quality == stack.Quality && item.m_worldLevel == stack.WorldLevel;

        /// <summary>Room for <paramref name="stack"/> the way Inventory.AddItem(ItemData) fills it: same-kind stacks, then empty slots.</summary>
        public static int RoomFor(Inventory inventory, ItemStack stack)
        {
            GameObject prefab = Prefab(stack);
            if (inventory == null || prefab == null) return 0;
            int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            int room = inventory.GetEmptySlots() * maxStack;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (SameKind(item, stack)) room += Mathf.Max(0, maxStack - item.m_stack);
            return room;
        }

        public static int Count(Inventory inventory, ItemStack stack)
        {
            int count = 0;
            if (inventory == null) return 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (SameKind(item, stack)) count += item.m_stack;
            return count;
        }

        /// <summary>Removes identical items first, then others of the same kind; returns the amount removed.</summary>
        public static int RemoveFrom(Inventory inventory, ItemStack stack, int amount)
        {
            if (inventory == null || amount <= 0) return 0;
            var identical = new List<ItemDrop.ItemData>();
            var similar = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
            {
                if (!SameKind(item, stack)) continue;
                if (FromItemData(item).StacksWith(stack)) identical.Add(item);
                else similar.Add(item);
            }
            int left = amount;
            foreach (ItemDrop.ItemData item in identical.Concat(similar))
            {
                if (left <= 0) break;
                int take = Mathf.Min(left, item.m_stack);
                if (inventory.RemoveItem(item, take)) left -= take;
            }
            return amount - left;
        }

        public static bool IsTransportable(ItemDrop.ItemData item) =>
            ItemRules.IsTransportable((GameItemType)(int)item.m_shared.m_itemType, item.IsEquipable());

        public static bool IsTransportable(ItemStack stack)
        {
            ItemDrop drop = Prefab(stack)?.GetComponent<ItemDrop>();
            return drop != null && IsTransportable(drop.m_itemData);
        }

        public static void RefreshShared(ItemStack stack)
        {
            ItemDrop drop = Prefab(stack)?.GetComponent<ItemDrop>();
            if (drop == null) return;
            stack.UnitWeight = drop.m_itemData.m_shared.m_weight;
            stack.MaxStack = drop.m_itemData.m_shared.m_maxStackSize;
        }

        /// <returns>The amount actually added, measured by counting the inventory before and after.</returns>
        public static int AddTo(Inventory inventory, ItemStack stack)
        {
            GameObject prefab = Prefab(stack);
            if (inventory == null || prefab == null) return 0;

            int before = Count(inventory, stack);
            int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            int left = stack.Amount;
            while (left > 0)
            {
                // AddItem keeps what it already stacked when the rest does not fit, so offer only what fits, by the
                // same quality-aware rule AddItem uses (CanAddItem ignores quality).
                int amount = Mathf.Min(left, Mathf.Min(maxStack, RoomFor(inventory, stack)));
                if (amount <= 0 || !inventory.AddItem(Create(prefab, stack, amount))) break;
                left -= amount;
            }
            return Count(inventory, stack) - before;
        }

        /// <returns>The amount dropped on the ground (0 for an unknown item prefab, which is logged by the caller).</returns>
        public static int Drop(ItemStack stack, Vector3 position)
        {
            GameObject prefab = Prefab(stack);
            if (prefab == null) return 0;
            int left = stack.Amount;
            int maxStack = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            while (left > 0)
            {
                int amount = Mathf.Min(left, maxStack);
                ItemDrop.DropItem(Create(prefab, stack, amount), amount, position + Random.insideUnitSphere * 0.3f, Quaternion.identity);
                left -= amount;
            }
            return stack.Amount;
        }

        private static ItemDrop.ItemData Create(GameObject prefab, ItemStack stack, int amount)
        {
            ItemDrop.ItemData item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = amount;
            item.m_quality = stack.Quality;
            item.m_variant = stack.Variant;
            item.m_worldLevel = stack.WorldLevel;
            item.m_cheated = stack.Cheated;
            item.m_crafterID = stack.CrafterId;
            item.m_crafterName = stack.CrafterName ?? string.Empty;
            item.m_customData = new Dictionary<string, string>(stack.CustomData ?? new Dictionary<string, string>());
            return item;
        }

        internal static GameObject Prefab(ItemStack stack) =>
            string.IsNullOrEmpty(stack.Prefab) || ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(stack.Prefab);
    }
}
