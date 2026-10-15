using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using Haugbui.Core.Persistence;
using Haugbui.Core.Production;
using Haugbui.Core.Storage;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Integration.StationAdapters;
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
        private readonly List<MonoBehaviour> _stationQuery = new List<MonoBehaviour>();
        private Dictionary<string, int> _reserved;
        private readonly List<(string Guid, Vec3 Position, float Radius)> _territories = new List<(string, Vec3, float)>();

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

        /// <summary>HB-T-069: the Production toggle is on and the tomb has the unlock level.</summary>
        public bool Production;

        /// <summary>
        /// HB-121: world seconds left before the Haugbui must head home (the dawn return lead); infinite with the Altar.
        /// </summary>
        public double WorkSecondsLeft;

        /// <summary>Loaded stations in the radius whose enabled job this tomb owns or may claim.</summary>
        public readonly List<StationWork> Stations = new List<StationWork>();

        /// <summary>
        /// HB-155 (TerritoryMode): the target belongs to this tomb, the nearest of its owner's tombs covering it. Storage
        /// chests used as sources are shared and never pass through here.
        /// </summary>
        public bool Mine(Vector3 position) =>
            _territories.Count <= 1 || Territory.OwnerOf(WorldRegistry.ToVec3(position), _territories) is string owner && owner == Holder;

        /// <summary>HB-087: what open Delivery Chest lines still wait for, by item prefab.</summary>
        public int Reserved(string prefab)
        {
            if (_reserved == null) _reserved = DeliveryLedger.Reserved(DeliveryChests.Select(d => d.Chest.Ledger));
            return _reserved.TryGetValue(prefab, out int n) ? n : 0;
        }

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
            Stations.Clear();
            _reserved = null;
            FindTerritories(tomb);

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

                // Deposits, Collection and Delivery Chests are work targets of one territory; plain chests stay shared sources.
                bool mine = Mine(container.transform.position);
                bool collection = IsCollectionChest(container);
                destination &= mine;
                source &= mine || !collection;
                if (!mine) delivery = null;
                if (!destination && !source && delivery == null) continue;

                ContainerSnapshot snapshot = Snapshot(container);
                Targets[snapshot.Id] = container;
                if (destination) Destinations.Add(snapshot);
                if (source) Sources.Add(snapshot);
                if (delivery != null) DeliveryChests.Add((snapshot, delivery));
                if (source && collection) CollectionChests.Add(snapshot);
            }

            ShiftTimer timer = tomb.Timer;
            WorkSecondsLeft = tomb.FullTime ? double.PositiveInfinity : timer.InShift ? timer.Seconds : 0;
            Production = ProductionGate.Enabled(tomb);
            if (Production) FindStations();
        }

        private void FindStations()
        {
            double now = TimeSource.Current.Now;
            double takeover = HaugbuiPlugin.Instance.Settings.JobTakeoverMinutes.Value * 60.0;
            _stationQuery.Clear();
            WorldRegistry.Stations.Query(WorldRegistry.ToVec3(Center), Radius, _stationQuery);
            foreach (MonoBehaviour station in _stationQuery)
            {
                IStationAdapter adapter = StationAdapterRegistry.For(station);
                ZNetView view = adapter?.View(station);
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                if (!Policy.WardsAllow(station.transform.position, OwnerId) || !Mine(station.transform.position)) continue;
                StationJobState job = StationJobs.Read(station, out PersistentSlot<StationJobState> slot);
                if (job == null || !slot.CanWrite) continue;
                StationRecipe recipe = adapter.Recipes(station).FirstOrDefault(r => r.Input == job.InputPrefab && r.Output == job.OutputPrefab);
                if (recipe == null) continue;
                // A disabled job still owes the products of what the Haugbui already put in; it collects them.
                if (!job.Enabled && ProductionJob.Planned(job, recipe.Yield) <= job.Produced) continue;

                // With territories the station is ours (Mine above), even if another tomb of this owner claimed it first.
                bool ourTerritory = _territories.Count > 1 && _territories.Any(t => t.Guid == job.TombGuid);
                bool present = !ourTerritory && OwnerPresent(job.TombGuid, station.transform.position);
                if (!ourTerritory && JobOwnership.Check(job, Holder, now, takeover, present) == OwnershipVerdict.Foreign) continue;
                // HB-118: claiming is a real change; a probe only asks whether there is work.
                if (!Probe && JobOwnership.Touch(job, Holder, now)) StationJobs.Save(station, slot, job);

                var work = new StationWork
                {
                    Station = station, Adapter = adapter, Id = StationAdapterRegistry.IdOf(station), Job = job, Slot = slot,
                    Recipe = recipe, State = adapter.Read(station), Position = WorldRegistry.ToVec3(station.transform.position),
                };
                if (!string.IsNullOrEmpty(recipe.Output)) FindOutputs(work);
                Stations.Add(work);
                Targets[work.Id] = station;
            }
        }

#if DEBUG
        /// <summary>Self-test suites written before territories run without them.</summary>
        internal static bool TerritorySuspendedForTests { get; set; }
#else
        private const bool TerritorySuspendedForTests = false;
#endif

        private void FindTerritories(TombController tomb)
        {
            _territories.Clear();
            if (!HaugbuiPlugin.Instance.Settings.TerritoryMode.Value || TerritorySuspendedForTests) return;
            foreach (TombController other in TombController.All)
            {
                if (other == null || other.State == null || other.State.OwnerId != OwnerId) continue;
                _territories.Add((other.Guid, WorldRegistry.ToVec3(other.transform.position), other.Attributes.Radius));
            }
        }

        private bool OwnerPresent(string tomb, Vector3 station)
        {
            if (string.IsNullOrEmpty(tomb) || tomb == Holder) return false;
            foreach (TombController other in TombController.All)
            {
                if (other == null || other.Guid != tomb || other.State == null || !ProductionGate.Enabled(other)) continue;
                if (Vector3.Distance(other.transform.position, station) <= other.Attributes.Radius) return true;
            }
            return false;
        }

        private static void FindOutputs(StationWork work)
        {
            Vector3 point = work.Adapter.OutputPoint(work.Station);
            float radius = work.Adapter.OutputRadius(work.Station);
            float radiusSq = radius * radius;
            foreach (ItemDrop drop in WorldRegistry.ItemDrops)
            {
                if (!DropEndpoint.IsLive(drop) || (drop.transform.position - point).sqrMagnitude > radiusSq) continue;
                if (ItemBridge.PrefabName(drop.m_itemData) != work.Recipe.Output) continue;
                work.Outputs.Add(drop);
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
