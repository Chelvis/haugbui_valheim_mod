using System.Collections.Generic;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>What a Haugbui knows about its area at one planning tick. Built only from loaded objects (HB-033).</summary>
    internal sealed class WorkContext
    {
        private readonly List<Container> _query = new List<Container>();

        public TombController Tomb;
        public long OwnerId;
        public Vec3 From;
        public double Now;
        public CarriedInventory Carried;
        public RetryBackoff NoDestination;

        /// <summary>Usable storage destinations in the radius, tomb storage excluded.</summary>
        public readonly List<ContainerSnapshot> Destinations = new List<ContainerSnapshot>();

        public readonly Dictionary<string, Container> ById = new Dictionary<string, Container>();
        public ContainerSnapshot TombStorage;

        public void Refresh(TombController tomb, IAccessPolicy policy)
        {
            Tomb = tomb;
            OwnerId = tomb.State.OwnerId;
            From = WorldRegistry.ToVec3(tomb.Creature != null ? tomb.Creature.transform.position : tomb.transform.position);
            Now = TimeSource.Current.Now;
            Carried = tomb.Carried;
            NoDestination = tomb.Worker.NoDestination;
            Destinations.Clear();
            ById.Clear();

            Container own = tomb.GetComponent<Container>();
            TombStorage = own != null ? Snapshot(own) : null;
            if (own != null) ById[TombStorage.Id] = own;

            _query.Clear();
            WorldRegistry.QueryContainers(tomb.transform.position, tomb.Attributes.Radius, _query);
            foreach (Container container in _query)
            {
                if (container == null || container == own) continue;
                if (new ContainerEndpoint(container).Unavailable() != null) continue;
                if (policy.CheckContainer(container, OwnerId, ContainerUse.StorageDestination) != ContainerVerdict.Allowed) continue;
                ContainerSnapshot snapshot = Snapshot(container);
                Destinations.Add(snapshot);
                ById[snapshot.Id] = container;
            }
        }

        public static ContainerSnapshot Snapshot(Container container)
        {
            Inventory inventory = container.GetInventory();
            var snapshot = new ContainerSnapshot
            {
                Id = ContainerEndpoint.IdOf(container),
                Position = WorldRegistry.ToVec3(container.transform.position),
                Slots = inventory.GetWidth() * inventory.GetHeight(),
            };
            foreach (ItemDrop.ItemData item in inventory.GetAllItems()) snapshot.Items.Add(ItemBridge.FromItemData(item));
            return snapshot;
        }
    }
}
