using System.Collections.Generic;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Registry;
using Haugbui.Structures;
using UnityEngine;
using Reservations = Haugbui.Coordination.Reservations;

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

        /// <summary>Where deliveries may take from (HB-084): storage and Collection Chests, never tombs or Delivery Chests.</summary>
        public readonly List<ContainerSnapshot> Sources = new List<ContainerSnapshot>();

        public readonly List<ContainerSnapshot> CollectionChests = new List<ContainerSnapshot>();
        public readonly List<(ContainerSnapshot Snapshot, DeliveryChest Chest)> DeliveryChests = new List<(ContainerSnapshot, DeliveryChest)>();

        /// <summary>When the Collection Chests are looked at next (HB-071), world time.</summary>
        public double NextCollectionScan;

        /// <summary>Every object a step of this tick's plans may target, by ZDO id.</summary>
        public readonly Dictionary<string, MonoBehaviour> Targets = new Dictionary<string, MonoBehaviour>();
        public ContainerSnapshot TombStorage;

        public Vector3 Center;
        public float Radius;
        public IAccessPolicy Policy;
        public string Holder;

        /// <summary>Only asking whether work exists (HB-167): providers must not change the worker's state.</summary>
        public bool Probe;

        public bool GroundItems;
        public bool Harvesting;
        public HarvestOptions HarvestOptions;

        /// <summary>Drops of this Haugbui's own harvests not picked up yet (HB-093).</summary>
        public IReadOnlyDictionary<string, ItemDrop> OwnDrops;

        public bool IsFree(string key) => Reservations.Table.IsAvailable(key, Holder);

        public void Refresh(TombController tomb, IAccessPolicy policy, bool probe = false)
        {
            Tomb = tomb;
            Probe = probe;
            Policy = policy;
            Holder = tomb.Guid;
            OwnerId = tomb.State.OwnerId;
            Center = tomb.transform.position;
            Radius = tomb.Attributes.Radius;
            GroundItems = tomb.State.Has(TombToggles.GroundItems);
            Harvesting = tomb.State.Has(TombToggles.Harvesting);
            Config.HaugbuiConfig settings = HaugbuiPlugin.Instance.Settings;
            HarvestOptions = new HarvestOptions
            {
                IncludeWildPickables = settings.HarvestWildPickables.Value,
                IncludeSapCollectors = settings.HarvestSapCollectors.Value,
            };
            OwnDrops = tomb.Worker.OwnDrops;
            From = WorldRegistry.ToVec3(tomb.Creature != null ? tomb.Creature.transform.position : tomb.transform.position);
            Now = TimeSource.Current.Now;
            Carried = tomb.Carried;
            NoDestination = tomb.Worker.NoDestination;
            NextCollectionScan = tomb.Worker.NextCollectionScan;
            Destinations.Clear();
            Sources.Clear();
            CollectionChests.Clear();
            DeliveryChests.Clear();
            Targets.Clear();

            Container own = tomb.GetComponent<Container>();
            TombStorage = own != null ? Snapshot(own) : null;
            if (own != null) Targets[TombStorage.Id] = own;

            _query.Clear();
            WorldRegistry.QueryContainers(Center, Radius, _query);
            foreach (Container container in _query)
            {
                if (container == null || container == own) continue;
                if (new ContainerEndpoint(container).Unavailable() != null) continue;
                bool destination = policy.CheckContainer(container, OwnerId, ContainerUse.StorageDestination) == ContainerVerdict.Allowed;
                bool source = policy.CheckContainer(container, OwnerId, ContainerUse.StorageSource) == ContainerVerdict.Allowed;
                DeliveryChest delivery = container.GetComponent<DeliveryChest>();
                if (delivery != null && policy.CheckContainer(container, OwnerId, ContainerUse.DeliveryTarget) != ContainerVerdict.Allowed) delivery = null;
                if (!destination && !source && delivery == null) continue;

                ContainerSnapshot snapshot = Snapshot(container);
                Targets[snapshot.Id] = container;
                if (destination) Destinations.Add(snapshot);
                if (source) Sources.Add(snapshot);
                if (delivery != null) DeliveryChests.Add((snapshot, delivery));
                if (source && IsCollectionChest(container)) CollectionChests.Add(snapshot);
            }
        }

        private static bool IsCollectionChest(Container container)
        {
            HaugbuiStructure structure = container.GetComponent<HaugbuiStructure>();
            return structure != null && structure.Kind == StructureKind.CollectionChest;
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
