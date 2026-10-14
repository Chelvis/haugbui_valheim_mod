using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;

namespace Haugbui.Core.Harvest
{
    /// <summary>Facts about one item lying on the ground, read from the game.</summary>
    public struct GroundItemFacts
    {
        public double AgeSeconds;
        public float NearestPlayerDistance;
        public bool IsPiece;
        public bool Transportable;
        public bool OwnedHere;
        public bool WardAllows;
    }

    public enum GroundItemVerdict
    {
        Eligible,
        TooYoung,
        PlayerNearby,
        Piece,
        NotTransportable,
        NotOwned,
        Ward
    }

    /// <summary>HB-091/092: which ground items a Haugbui may pick up (the destination is checked by the planner).</summary>
    public static class GroundItemRules
    {
        public const double MinAgeSeconds = 10;
        public const float PlayerDistance = 3f;

        public static GroundItemVerdict Check(GroundItemFacts facts)
        {
            if (facts.IsPiece) return GroundItemVerdict.Piece;
            if (!facts.Transportable) return GroundItemVerdict.NotTransportable;
            if (facts.AgeSeconds < MinAgeSeconds) return GroundItemVerdict.TooYoung;
            if (facts.NearestPlayerDistance < PlayerDistance) return GroundItemVerdict.PlayerNearby;
            if (!facts.OwnedHere) return GroundItemVerdict.NotOwned;
            if (!facts.WardAllows) return GroundItemVerdict.Ward;
            return GroundItemVerdict.Eligible;
        }
    }

    public enum HarvestTargetKind
    {
        Pickable,
        Beehive,
        SapCollector
    }

    /// <summary>Facts about one harvestable object, read from the game.</summary>
    public struct HarvestFacts
    {
        public HarvestTargetKind Kind;

        /// <summary>Pickable.m_harvestable: the scythe's flag, set on cultivated crops.</summary>
        public bool Cultivated;

        /// <summary>Pickable: ripe and not picked. Extractors: level ≥ 1.</summary>
        public bool Ready;

        /// <summary>Picking it angers nearby creatures (Pickable.m_aggravateRange &gt; 0).</summary>
        public bool Aggravates;

        public bool ProductTransportable;
        public bool WardAllows;
    }

    public struct HarvestOptions
    {
        public bool IncludeWildPickables;
        public bool IncludeSapCollectors;
    }

    /// <summary>HB-101..105: mature crops, full hives and nests; wild pickables and sap only by config; never anything else.</summary>
    public static class HarvestRules
    {
        public static bool IsTarget(HarvestFacts facts, HarvestOptions options)
        {
            if (!facts.Ready || !facts.ProductTransportable || !facts.WardAllows) return false;
            switch (facts.Kind)
            {
                case HarvestTargetKind.Pickable:
                    return !facts.Aggravates && (facts.Cultivated || options.IncludeWildPickables);
                case HarvestTargetKind.Beehive:
                    return true;
                case HarvestTargetKind.SapCollector:
                    return options.IncludeSapCollectors;
                default:
                    return false;
            }
        }
    }

    public sealed class GroundItem
    {
        public string Id = string.Empty;
        public Vec3 Position;
        public ItemStack Item;
    }

    /// <summary>
    /// HB-T-054: one trip collects nearby drops: nearest first, then the nearest to the last one within
    /// <see cref="MaxHop"/>, while the carried inventory can take them whole.
    /// </summary>
    public static class PickupRoute
    {
        public const float MaxHop = 10f;
        public const int MaxStops = 8;

        public static List<GroundItem> Plan(IEnumerable<GroundItem> candidates, Vec3 from, CarriedInventory carried)
        {
            var left = new List<GroundItem>(candidates);
            var simulated = new CarriedInventory(carried.Slots, carried.Capacity);
            var copies = new List<ItemStack>();
            foreach (ItemStack stack in carried.Stacks) copies.Add(stack.Copy(stack.Amount));
            simulated.Restore(copies);

            var route = new List<GroundItem>();
            Vec3 at = from;
            while (left.Count > 0 && route.Count < MaxStops)
            {
                int best = -1;
                float bestDistance = float.MaxValue;
                for (int i = 0; i < left.Count; i++)
                {
                    float d = Vec3.Distance(at, left[i].Position);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = i;
                    }
                }
                if (route.Count > 0 && bestDistance > MaxHop) break;
                GroundItem next = left[best];
                left.RemoveAt(best);
                if (simulated.CanAccept(next.Item) < next.Item.Amount) continue;
                simulated.Add(next.Item.Copy(next.Item.Amount));
                route.Add(next);
                at = next.Position;
            }
            return route;
        }
    }
}
