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

        /// <summary>Same data as <see cref="ItemStack.StacksWith"/>: merging the two changes nothing (HB-061).</summary>
        public static bool Identical(ItemDrop.ItemData item, ItemStack stack)
        {
            if (!SameKind(item, stack) || item.m_variant != stack.Variant || item.m_cheated != stack.Cheated
                || item.m_crafterID != stack.CrafterId || (item.m_crafterName ?? string.Empty) != (stack.CrafterName ?? string.Empty))
                return false;
            Dictionary<string, string> a = item.m_customData, b = stack.CustomData;
            int countA = a?.Count ?? 0, countB = b?.Count ?? 0;
            if (countA != countB) return false;
            if (countA == 0) return true;
            foreach (KeyValuePair<string, string> pair in a)
                if (!b.TryGetValue(pair.Key, out string value) || value != pair.Value) return false;
            return true;
        }

        /// <summary>Room on identical stacks plus empty slots, the way <see cref="AddTo"/> fills them.</summary>
        public static int RoomFor(Inventory inventory, ItemStack stack)
        {
            GameObject prefab = Prefab(stack);
            if (inventory == null || prefab == null) return 0;
            int maxStack = MaxStack(prefab);
            int room = inventory.GetEmptySlots() * maxStack;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (Identical(item, stack)) room += Mathf.Max(0, maxStack - item.m_stack);
            return room;
        }

        /// <summary>Items identical to <paramref name="stack"/>; the transfer unit, so flags never change on the way.</summary>
        public static int Count(Inventory inventory, ItemStack stack)
        {
            int count = 0;
            if (inventory == null) return 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (Identical(item, stack)) count += item.m_stack;
            return count;
        }

        /// <summary>Every item of the prefab, whatever its data.</summary>
        public static int CountPrefab(Inventory inventory, string prefab)
        {
            int count = 0;
            if (inventory == null) return 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (PrefabName(item) == prefab) count += item.m_stack;
            return count;
        }

        /// <summary>Removes only items identical to <paramref name="stack"/>; returns the amount removed.</summary>
        public static int RemoveFrom(Inventory inventory, ItemStack stack, int amount)
        {
            if (inventory == null || amount <= 0) return 0;
            var identical = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (Identical(item, stack)) identical.Add(item);
            int left = amount;
            foreach (ItemDrop.ItemData item in identical)
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

        /// <summary>
        /// Tops up identical stacks, then fills empty slots. Never the public Inventory.AddItem overloads: they merge by
        /// name, quality and world level only, and AddItem(ItemData) marks the whole target stack cheated.
        /// </summary>
        /// <returns>The amount actually added, measured by counting the inventory before and after.</returns>
        public static int AddTo(Inventory inventory, ItemStack stack)
        {
            GameObject prefab = Prefab(stack);
            if (inventory == null || prefab == null || stack.Amount <= 0) return 0;

            int before = Count(inventory, stack);
            int maxStack = MaxStack(prefab);
            int left = stack.Amount;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems().ToList())
            {
                if (left <= 0) break;
                if (!Identical(item, stack) || item.m_stack >= maxStack) continue;
                left -= PlaceAt(inventory, prefab, stack, Mathf.Min(left, maxStack - item.m_stack), item.m_gridPos);
            }
            for (int y = 0; y < inventory.GetHeight() && left > 0; y++)
            {
                for (int x = 0; x < inventory.GetWidth() && left > 0; x++)
                {
                    if (inventory.GetItemAt(x, y) != null) continue;
                    left -= PlaceAt(inventory, prefab, stack, Mathf.Min(left, maxStack), new Vector2i(x, y));
                }
            }
            int added = Count(inventory, stack) - before;
            // The slot overload only raises Changed (which saves the container) while a local player exists.
            if (added > 0 && !Player.m_localPlayerExists) inventory.Changed();
            return added;
        }

        /// <summary>Inventory.AddItem(item, amount, x, y) stacks only onto the item in that slot and never touches m_cheated.</summary>
        private static int PlaceAt(Inventory inventory, GameObject prefab, ItemStack stack, int amount, Vector2i slot)
        {
            if (amount <= 0) return 0;
            ItemDrop.ItemData item = Create(prefab, stack, amount);
            inventory.AddItem(item, amount, slot.x, slot.y);
            return amount - item.m_stack;
        }

        private static int MaxStack(GameObject prefab) => Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);

        /// <returns>The amount dropped on the ground (0 for an unknown item prefab, which is logged by the caller).</returns>
        public static int Drop(ItemStack stack, Vector3 position)
        {
            GameObject prefab = Prefab(stack);
            if (prefab == null) return 0;
            int left = stack.Amount;
            int maxStack = MaxStack(prefab);
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
