using System;
using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>Reads a player-built material pile and returns its stack before the piece is removed.</summary>
    internal static class PileGateway
    {
        /// <summary>Times the refund was in hand while the piece was still standing.</summary>
        internal static int RefundsAccountedBeforeRemoval;

        public static bool TrySnapshot(Piece piece, out MaterialPile pile)
        {
            pile = null;
            if (piece == null || !piece.IsPlacedByPlayer()) return false;
            Piece.Requirement[] requirements = piece.m_resources;
            if (requirements == null || requirements.Length != 1) return false;
            Piece.Requirement requirement = requirements[0];
            ItemDrop item = requirement?.m_resItem;
            if (item == null || item.m_itemData?.m_shared == null) return false;
            int amount = requirement.m_amount;
            int maxStack = item.m_itemData.m_shared.m_maxStackSize;
            bool material = item.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Material;
            if (!PileRules.IsPile(true, 1, requirement.m_recover, material, amount, maxStack)) return false;
            ZNetView view = piece.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || view.GetZDO() == null) return false;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return false;

            ItemStack refund = ItemBridge.FromItemData(item.m_itemData);
            refund.Prefab = Utils.GetPrefabName(item.gameObject);
            refund.Amount = amount;
            refund.Cheated = view.GetZDO().IsCheated() && !PlayerProfile.s_bypassCheatChecks;
            pile = new MaterialPile
            {
                Id = view.GetZDO().m_uid.ToString(),
                Position = WorldRegistry.ToVec3(piece.transform.position),
                Refund = refund,
            };
            return true;
        }

        public static bool Eligible(Piece piece, long ownerId, IAccessPolicy policy, Func<Vector3, bool> territory)
        {
            if (!TrySnapshot(piece, out _)) return false;
            if (policy != null && !policy.WardsAllow(piece.transform.position, ownerId)) return false;
            return territory == null || territory(piece.transform.position);
        }

        /// <summary>The stack is in the bag, or on the ground, before the piece goes.</summary>
        public static bool Dismantle(Piece piece, CarriedInventory carried)
        {
            if (!TrySnapshot(piece, out MaterialPile pile) || carried == null) return false;
            ZNetView view = piece.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            if (!view.IsOwner()) return false;
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (wear == null) return false;

            ItemStack refund = pile.Refund;
            int added = carried.Add(refund.Copy(refund.Amount));
            var spawned = new List<GameObject>();
            int dropped = Spawn(refund, refund.Amount - added, piece, spawned);
            if (added + dropped != refund.Amount)
            {
                if (added > 0) carried.Remove(refund, added);
                foreach (GameObject go in spawned)
                    if (go != null && ZNetScene.instance != null) ZNetScene.instance.Destroy(go);
                return false;
            }
            RefundsAccountedBeforeRemoval++;
            wear.Remove(true);
            return true;
        }

        private static int Spawn(ItemStack refund, int amount, Piece piece, List<GameObject> spawned)
        {
            if (amount <= 0) return 0;
            GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(refund.Prefab) : null;
            if (prefab == null) return 0;
            int left = amount;
            int made = 0;
            Vector3 at = piece.transform.position + Vector3.up * piece.m_returnResourceHeightOffset;
            while (left > 0)
            {
                int n = Math.Min(left, Math.Max(1, refund.MaxStack));
                ItemDrop drop = UnityEngine.Object.Instantiate(prefab, at, Quaternion.identity).GetComponent<ItemDrop>();
                if (drop == null) return made;
                drop.SetStack(n);
                ItemDrop.OnCreateNew(drop);
                if (refund.Cheated) drop.m_itemData.m_cheated = true;
                spawned.Add(drop.gameObject);
                made += n;
                left -= n;
            }
            return made;
        }
    }
}
