using Haugbui.Core.Creature;
using Haugbui.Core.Storage;
using Haugbui.Creature;

namespace Haugbui.Integration
{
    /// <summary>
    /// An item lying on the ground as a transfer source. Only the ZDO owner may change it (ItemDrop.CanPickup), so a
    /// foreign drop asks for ownership and is retried later.
    /// </summary>
    internal sealed class DropEndpoint : IItemEndpoint
    {
        private readonly ItemDrop _drop;

        public DropEndpoint(ItemDrop drop)
        {
            _drop = drop;
            Id = IdOf(drop);
        }

        public string Id { get; }

        public static string IdOf(ItemDrop drop) =>
            drop != null && drop.m_nview != null && drop.m_nview.GetZDO() != null ? drop.m_nview.GetZDO().m_uid.ToString() : "drop:none";

        public static bool IsLive(ItemDrop drop) => drop != null && drop.m_nview != null && drop.m_nview.IsValid();

        /// <summary>The drop's current data (another peer or AutoStackItems may have changed it).</summary>
        public static ItemStack Read(ItemDrop drop)
        {
            drop.Load();
            return ItemBridge.FromItemData(drop.m_itemData);
        }

        public string Unavailable()
        {
            if (!IsLive(_drop)) return "gone";
            if (!_drop.CanPickup(autoPickupDelay: false))
            {
                _drop.RequestOwn();
                return "not owner";
            }
            return null;
        }

        public int Count(ItemStack item)
        {
            if (!IsLive(_drop)) return 0;
            _drop.Load();
            return ItemBridge.Identical(_drop.m_itemData, item) ? _drop.m_itemData.m_stack : 0;
        }

        public int CountKind(ItemStack item)
        {
            if (!IsLive(_drop)) return 0;
            _drop.Load();
            return ItemBridge.SameKind(_drop.m_itemData, item) ? _drop.m_itemData.m_stack : 0;
        }

        /// <summary>A drop is only ever a source; nothing is added to it, so nothing needs rolling back.</summary>
        public int RemoveKind(ItemStack item, int amount) => Remove(item, amount);

        public int RoomFor(ItemStack item) => 0;

        public int Add(ItemStack item) => 0;

        public int Remove(ItemStack item, int amount)
        {
            int available = Count(item);
            int n = amount < available ? amount : available;
            if (n <= 0) return 0;
            if (n >= available) _drop.m_nview.Destroy();
            else _drop.SetStack(available - n);
            return n;
        }
    }
}
