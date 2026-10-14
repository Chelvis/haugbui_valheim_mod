using Haugbui.Core.Creature;
using Haugbui.Core.Storage;
using Haugbui.Creature;

namespace Haugbui.Integration
{
    /// <summary>A vanilla container as a transfer endpoint. Only the ZDO owner edits it, and never while a player has it open.</summary>
    internal sealed class ContainerEndpoint : IItemEndpoint
    {
        private readonly Container _container;

        public ContainerEndpoint(Container container)
        {
            _container = container;
            Id = IdOf(container);
        }

        public string Id { get; }

        public static string IdOf(Container container) =>
            container != null && container.m_nview != null && container.m_nview.GetZDO() != null
                ? container.m_nview.GetZDO().m_uid.ToString()
                : "container:none";

        public string Unavailable()
        {
            if (_container == null) return "gone";
            ZNetView view = _container.m_nview;
            if (view == null || !view.IsValid()) return "not loaded";
            if (!view.IsOwner()) return "not owner";
            if (_container.IsInUse()) return "in use";
            // Until its first Load the inventory is empty in memory, and saving it would wipe the stored items. Later
            // revisions are not compared: any key on the piece (support, snow, Haugbui state) bumps them.
            if (_container.m_lastRevision == uint.MaxValue) return "loading";
            return null;
        }

        public int Count(ItemStack item) => ItemBridge.Count(_container.GetInventory(), item);
        public int RoomFor(ItemStack item) => ItemBridge.RoomFor(_container.GetInventory(), item);
        public int Add(ItemStack item) => ItemBridge.AddTo(_container.GetInventory(), item);
        public int Remove(ItemStack item, int amount) => ItemBridge.RemoveFrom(_container.GetInventory(), item, amount);
    }
}
