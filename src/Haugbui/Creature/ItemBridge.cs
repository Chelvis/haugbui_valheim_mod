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
            UnitWeight = item.m_shared.m_weight,
            MaxStack = item.m_shared.m_maxStackSize,
        };

        public static bool IsTransportable(ItemDrop.ItemData item) =>
            ItemRules.IsTransportable((GameItemType)(int)item.m_shared.m_itemType, item.IsEquipable());

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

            ItemDrop.ItemData template = prefab.GetComponent<ItemDrop>().m_itemData;
            string name = template.m_shared.m_name;
            int before = inventory.CountItems(name, stack.Quality, matchWorldLevel: false);
            int maxStack = Mathf.Max(1, template.m_shared.m_maxStackSize);
            int left = stack.Amount;
            while (left > 0)
            {
                // CanAddItem is all-or-nothing, so offer only what fits: room on matching stacks, then empty slots.
                int room = inventory.FindFreeStackSpace(name, stack.WorldLevel) + inventory.GetEmptySlots() * maxStack;
                int amount = Mathf.Min(left, Mathf.Min(maxStack, room));
                if (amount <= 0 || !inventory.AddItem(Create(prefab, stack, amount))) break;
                left -= amount;
            }
            return inventory.CountItems(name, stack.Quality, matchWorldLevel: false) - before;
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
            return item;
        }

        private static GameObject Prefab(ItemStack stack) =>
            string.IsNullOrEmpty(stack.Prefab) || ObjectDB.instance == null ? null : ObjectDB.instance.GetItemPrefab(stack.Prefab);
    }
}
