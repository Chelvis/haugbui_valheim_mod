using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;
using Haugbui.Core.Harvest;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Registry;
using UnityEngine;

namespace Haugbui.Brain
{
    /// <summary>Destination lookups shared by the gathering providers, cached per item kind for one planning tick.</summary>
    internal static class GatherDestinations
    {
        private static readonly Dictionary<string, bool> Cache = new Dictionary<string, bool>();
        private static WorkContext _for;
        private static double _at;

        /// <summary>HB-065/091: only what some chest can take now.</summary>
        public static bool Has(WorkContext context, ItemStack stack)
        {
            if (_for != context || _at != context.Now)
            {
                Cache.Clear();
                _for = context;
                _at = context.Now;
            }
            if (Cache.TryGetValue(stack.KindKey, out bool known)) return known;
            bool has = !context.NoDestination.IsWaiting(stack.KindKey, context.Now)
                       && StorageSelector.Plan(stack, stack.Amount, context.Destinations, context.From).Sum(a => a.Amount) > 0;
            Cache[stack.KindKey] = has;
            return has;
        }
    }

    /// <summary>HB-T-054: ground items in the radius that have a destination, collected in one trip.</summary>
    internal sealed class GroundItemProvider : ITaskProvider<WorkContext>
    {
        private readonly List<GroundItem> _candidates = new List<GroundItem>();
        private readonly Dictionary<string, ItemDrop> _drops = new Dictionary<string, ItemDrop>();

        public bool IsEnabled(WorkContext context) => context.GroundItems;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            _candidates.Clear();
            _drops.Clear();
            float radiusSq = context.Radius * context.Radius;
            foreach (ItemDrop drop in WorldRegistry.ItemDrops)
            {
                if (!DropEndpoint.IsLive(drop)) continue;
                Vector3 position = drop.transform.position;
                if ((position - context.Center).sqrMagnitude > radiusSq) continue;
                string id = DropEndpoint.IdOf(drop);
                if (context.OwnDrops.ContainsKey(id) || !context.IsFree(GatherKeys.Drop(id))) continue;
                if (GroundItemRules.Check(Facts(drop, context)) != GroundItemVerdict.Eligible) continue;
                ItemStack stack = DropEndpoint.Read(drop);
                if (!GatherDestinations.Has(context, stack)) continue;
                _candidates.Add(new GroundItem { Id = id, Position = WorldRegistry.ToVec3(position), Item = stack });
                _drops[id] = drop;
            }
            GatherKeys.AddRoute(context, output, _candidates, _drops, TaskKinds.Gather, TaskKinds.GatherPriority, StepKind.PickUp);
        }

        private static GroundItemFacts Facts(ItemDrop drop, WorkContext context)
        {
            Vector3 position = drop.transform.position;
            float nearest = float.MaxValue;
            foreach (Player player in Player.GetAllPlayers())
                if (player != null) nearest = Mathf.Min(nearest, Vector3.Distance(player.transform.position, position));
            return new GroundItemFacts
            {
                AgeSeconds = drop.GetTimeSinceSpawned(),
                NearestPlayerDistance = nearest,
                IsPiece = drop.IsPiece() || drop.InTar(),
                Transportable = ItemBridge.IsTransportable(drop.m_itemData),
                OwnedHere = drop.m_nview.IsOwner(),
                WardAllows = context.Policy.WardsAllow(position, context.OwnerId),
            };
        }
    }

    /// <summary>HB-T-055/056: ripe crops, full hives and nests (sap and wild pickables by config), nearest first.</summary>
    internal sealed class HarvestProvider : ITaskProvider<WorkContext>
    {
        private readonly List<MonoBehaviour> _query = new List<MonoBehaviour>();
        private readonly List<GroundItem> _candidates = new List<GroundItem>();
        private readonly Dictionary<string, MonoBehaviour> _targets = new Dictionary<string, MonoBehaviour>();

        public bool IsEnabled(WorkContext context) => context.Harvesting;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            _query.Clear();
            _candidates.Clear();
            _targets.Clear();
            WorldRegistry.Harvestables.Query(WorldRegistry.ToVec3(context.Center), context.Radius, _query);
            foreach (MonoBehaviour target in _query)
            {
                if (target == null) continue;
                string id = HarvestGateway.IdOf(target);
                if (!context.IsFree(GatherKeys.Harvest(id))) continue;
                ItemStack product = HarvestGateway.ExpectedProduct(target);
                bool ward = context.Policy.WardsAllow(target.transform.position, context.OwnerId);
                if (!HarvestRules.IsTarget(HarvestGateway.Facts(target, product, ward), context.HarvestOptions)) continue;
                if (!GatherDestinations.Has(context, product)) continue;
                _candidates.Add(new GroundItem { Id = id, Position = WorldRegistry.ToVec3(target.transform.position), Item = product });
                _targets[id] = target;
            }
            GatherKeys.AddRoute(context, output, _candidates, _targets, TaskKinds.Harvest, TaskKinds.HarvestPriority, StepKind.Harvest);
        }
    }

    /// <summary>HB-093: what its own harvests dropped and did not fit is picked up even with the ground toggle off.</summary>
    internal sealed class OwnDropsProvider : ITaskProvider<WorkContext>
    {
        private readonly List<GroundItem> _candidates = new List<GroundItem>();
        private readonly Dictionary<string, ItemDrop> _drops = new Dictionary<string, ItemDrop>();

        public bool IsEnabled(WorkContext context) => context.OwnDrops.Count > 0;

        public void Collect(WorkContext context, List<TaskCandidate> output)
        {
            _candidates.Clear();
            _drops.Clear();
            foreach (KeyValuePair<string, ItemDrop> own in context.OwnDrops)
            {
                ItemDrop drop = own.Value;
                if (!DropEndpoint.IsLive(drop) || !context.IsFree(GatherKeys.Drop(own.Key))) continue;
                ItemStack stack = DropEndpoint.Read(drop);
                if (!ItemBridge.IsTransportable(stack)) continue;
                _candidates.Add(new GroundItem { Id = own.Key, Position = WorldRegistry.ToVec3(drop.transform.position), Item = stack });
                _drops[own.Key] = drop;
            }
            GatherKeys.AddRoute(context, output, _candidates, _drops, TaskKinds.Harvest, TaskKinds.HarvestPriority + 1, StepKind.PickUp);
        }
    }

    internal static class GatherKeys
    {
        public static string Drop(string id) => "drop:" + id;
        public static string Harvest(string id) => "harvest:" + id;

        /// <summary>One candidate: a trip through the nearest targets whose items the carried inventory can take.</summary>
        public static void AddRoute<T>(WorkContext context, List<TaskCandidate> output, List<GroundItem> candidates,
            Dictionary<string, T> targets, string kind, int priority, StepKind step) where T : MonoBehaviour
        {
            if (candidates.Count == 0) return;
            List<GroundItem> route = PickupRoute.Plan(candidates, context.From, context.Carried);
            if (route.Count == 0) return;
            foreach (GroundItem stop in route) context.Targets[stop.Id] = targets[stop.Id];
            List<TaskStep> steps = route.Select(s => new TaskStep(step, s.Id, s.Item, s.Item.Amount)).ToList();
            output.Add(new TaskCandidate
            {
                Kind = kind,
                Priority = priority,
                Distance = Vec3.Distance(context.From, route[0].Position),
                Keys = route.Select(s => step == StepKind.Harvest ? Harvest(s.Id) : Drop(s.Id)).ToArray(),
                Build = () => new TaskPlan(kind, steps),
            });
        }
    }
}
