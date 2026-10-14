using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Geometry;

namespace Haugbui.Core.Structures
{
    public readonly struct BedInfo
    {
        public BedInfo(Vec3 position, long ownerId)
        {
            Position = position;
            OwnerId = ownerId;
        }

        public Vec3 Position { get; }
        public long OwnerId { get; }
    }

    public sealed class TombInfo
    {
        public TombInfo(string guid, Vec3 position, IEnumerable<StructureKind> boundUpgrades = null)
        {
            Guid = guid;
            Position = position;
            BoundUpgrades = new HashSet<StructureKind>(boundUpgrades ?? Enumerable.Empty<StructureKind>());
        }

        public string Guid { get; }
        public Vec3 Position { get; }
        public HashSet<StructureKind> BoundUpgrades { get; }
    }

    public sealed class UpgradeInfo
    {
        public UpgradeInfo(string id, StructureKind kind, Vec3 position, string boundTombGuid)
        {
            Id = id;
            Kind = kind;
            Position = position;
            BoundTombGuid = boundTombGuid;
        }

        public string Id { get; }
        public StructureKind Kind { get; }
        public Vec3 Position { get; }
        public string BoundTombGuid { get; }
    }

    public enum PlacementResult
    {
        Ok,
        NeedOwnBed,
        TombTooClose,
        OwnTombTooClose,
        NeedTomb,
        UpgradeExists,
        MaxTombs
    }

    public static class PlacementMessages
    {
        public static string KeyFor(PlacementResult result)
        {
            switch (result)
            {
                case PlacementResult.NeedOwnBed: return "haugbui_msg_need_bed";
                case PlacementResult.TombTooClose: return "haugbui_msg_tomb_too_close";
                case PlacementResult.OwnTombTooClose: return "haugbui_msg_tomb_too_close_own";
                case PlacementResult.NeedTomb: return "haugbui_msg_need_tomb";
                case PlacementResult.UpgradeExists: return "haugbui_msg_upgrade_exists";
                case PlacementResult.MaxTombs: return "haugbui_msg_max_tombs";
                default: return null;
            }
        }
    }

    public static class PlacementRules
    {
        public static PlacementResult CheckTomb(
            Vec3 position, long placerId, IEnumerable<BedInfo> beds, IEnumerable<Vec3> otherTombs,
            float bedDistance, float tombSpacing)
        {
            bool ownBedNear = beds.Any(b => b.OwnerId != 0 && b.OwnerId == placerId
                                            && Vec3.Distance(b.Position, position) <= bedDistance);
            if (!ownBedNear) return PlacementResult.NeedOwnBed;
            if (otherTombs.Any(t => Vec3.Distance(t, position) < tombSpacing)) return PlacementResult.TombTooClose;
            return PlacementResult.Ok;
        }

        /// <summary>HB-004: any tomb within <paramref name="tombSpacing"/>; the placer's own tombs within <paramref name="sameOwnerSpacing"/>.</summary>
        public static PlacementResult CheckTomb(
            Vec3 position, long placerId, IEnumerable<BedInfo> beds, IReadOnlyCollection<(Vec3 Position, long OwnerId)> otherTombs,
            float bedDistance, float tombSpacing, float sameOwnerSpacing)
        {
            PlacementResult result = CheckTomb(position, placerId, beds, otherTombs.Select(t => t.Position), bedDistance, tombSpacing);
            if (result != PlacementResult.Ok) return result;
            if (otherTombs.Any(t => t.OwnerId == placerId && Vec3.Distance(t.Position, position) < sameOwnerSpacing))
                return PlacementResult.OwnTombTooClose;
            return PlacementResult.Ok;
        }

        /// <summary>Picks the nearest tomb within range that does not have this upgrade kind yet.</summary>
        public static PlacementResult CheckUpgrade(
            StructureKind kind, Vec3 position, IEnumerable<TombInfo> tombs, float range, out TombInfo bindTo)
        {
            bindTo = null;
            List<TombInfo> inRange = tombs
                .Where(t => Vec3.Distance(t.Position, position) <= range)
                .OrderBy(t => Vec3.Distance(t.Position, position))
                .ToList();
            if (inRange.Count == 0) return PlacementResult.NeedTomb;

            bindTo = inRange.FirstOrDefault(t => !t.BoundUpgrades.Contains(kind));
            return bindTo == null ? PlacementResult.UpgradeExists : PlacementResult.Ok;
        }

        /// <summary>
        /// Binds upgrades whose tomb no longer exists to the nearest tomb in range that lacks their kind (a rebuilt
        /// tomb inherits the upgrades around it). Returns upgrade id → tomb guid for the upgrades that changed.
        /// </summary>
        public static IReadOnlyDictionary<string, string> RebindOrphans(
            IEnumerable<UpgradeInfo> upgrades, IReadOnlyList<TombInfo> tombs, float range)
        {
            var known = new HashSet<string>(tombs.Select(t => t.Guid));
            var taken = tombs.ToDictionary(t => t.Guid, t => new HashSet<StructureKind>(t.BoundUpgrades));
            var result = new Dictionary<string, string>();

            foreach (UpgradeInfo upgrade in upgrades.Where(u => string.IsNullOrEmpty(u.BoundTombGuid) || !known.Contains(u.BoundTombGuid))
                         .OrderBy(u => u.Id))
            {
                TombInfo target = tombs
                    .Where(t => Vec3.Distance(t.Position, upgrade.Position) <= range && !taken[t.Guid].Contains(upgrade.Kind))
                    .OrderBy(t => Vec3.Distance(t.Position, upgrade.Position))
                    .FirstOrDefault();
                if (target == null) continue;
                taken[target.Guid].Add(upgrade.Kind);
                result[upgrade.Id] = target.Guid;
            }
            return result;
        }

        /// <summary>Upgrade kinds that count for a tomb: bound to it and still within range.</summary>
        public static IEnumerable<StructureKind> CountedUpgrades(TombInfo tomb, IEnumerable<UpgradeInfo> upgrades, float range) =>
            upgrades.Where(u => u.BoundTombGuid == tomb.Guid && Vec3.Distance(u.Position, tomb.Position) <= range)
                .Select(u => u.Kind)
                .Distinct();
    }
}
