using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Geometry;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.Structures
{
    /// <summary>Loaded Haugbui structures and the tomb level/ability computation (HB-010, HB-013).</summary>
    internal static class StructureRegistry
    {
        private static readonly HashSet<HaugbuiStructure> Structures = new HashSet<HaugbuiStructure>();
        private static bool _dirty;

        public static float UpgradeRange { get; set; } = 8f;

        public static IEnumerable<HaugbuiStructure> Loaded => Structures.Where(s => s != null);
        public static IEnumerable<HaugbuiStructure> Tombs => Loaded.Where(s => s.Kind == StructureKind.Tomb);
        public static IEnumerable<HaugbuiStructure> Upgrades => Loaded.Where(s => TombProgression.IsUpgrade(s.Kind));

        public static void Register(HaugbuiStructure structure)
        {
            Structures.Add(structure);
            _dirty = true;
        }

        public static void Unregister(HaugbuiStructure structure)
        {
            if (Structures.Remove(structure)) _dirty = true;
        }

        public static void MarkDirty() => _dirty = true;

        public static HaugbuiStructure FindTomb(string guid) =>
            string.IsNullOrEmpty(guid) ? null : Tombs.FirstOrDefault(t => t.Guid == guid);

        public static Vec3 ToVec3(Vector3 v) => new Vec3(v.x, v.y, v.z);

        public static List<TombInfo> TombInfos()
        {
            List<UpgradeInfo> upgrades = UpgradeInfos();
            return Tombs.Select(t =>
            {
                var info = new TombInfo(t.Guid, ToVec3(t.transform.position));
                info.BoundUpgrades.UnionWith(PlacementRules.CountedUpgrades(info, upgrades, UpgradeRange));
                return info;
            }).ToList();
        }

        public static List<UpgradeInfo> UpgradeInfos() =>
            Upgrades.Select(u => new UpgradeInfo(u.Guid, u.Kind, ToVec3(u.transform.position), u.BoundTombGuid)).ToList();

        /// <summary>Called every frame by the plugin; recomputes at most once per frame and only after changes.</summary>
        public static void Update()
        {
            if (!_dirty || ZNetScene.instance == null) return;
            _dirty = false;
            RebindOrphans();
            RecomputeLevels();
            Creature.TombController.BindPendingSlots();
        }

        private static void RebindOrphans()
        {
            List<TombInfo> tombs = TombInfos();
            var known = new HashSet<string>(tombs.Select(t => t.Guid));
            List<UpgradeInfo> candidates = UpgradeInfos()
                .Where(u => string.IsNullOrEmpty(u.BoundTombGuid)
                            || (!known.Contains(u.BoundTombGuid) && !TombExistsInWorld(u.BoundTombGuid)))
                .ToList();
            if (candidates.Count == 0) return;

            foreach (KeyValuePair<string, string> change in PlacementRules.RebindOrphans(candidates, tombs, UpgradeRange))
            {
                HaugbuiStructure upgrade = Upgrades.FirstOrDefault(u => u.Guid == change.Key);
                if (upgrade == null || !upgrade.View.IsOwner()) continue;
                upgrade.Zdo.Set(ZdoKeys.TombGuid, change.Value);
                Log.Info($"{upgrade.Definition?.PieceId} rebound to tomb {change.Value}");
                if (upgrade.Kind == StructureKind.Longsword) CombatWarning.Show();
            }
        }

        private static void RecomputeLevels()
        {
            List<UpgradeInfo> upgrades = UpgradeInfos();
            foreach (HaugbuiStructure tomb in Tombs)
            {
                var info = new TombInfo(tomb.Guid, ToVec3(tomb.transform.position));
                TombProgress progress = TombProgression.Compute(PlacementRules.CountedUpgrades(info, upgrades, UpgradeRange));
                if (progress.Level == tomb.Progress.Level && progress.Abilities == tomb.Progress.Abilities) continue;

                tomb.Progress = progress;
                Log.Debug($"Tomb {tomb.Guid}: level {progress.Level}, abilities {progress.Abilities}");
                if (tomb.TombState != null && tomb.TombState.LevelCache != progress.Level)
                {
                    tomb.TombState.LevelCache = progress.Level;
                    tomb.SaveTombState();
                }
            }
        }

        /// <summary>Looks through all world data, so a tomb in an unloaded neighbour zone is not taken as destroyed.</summary>
        internal static bool TombExistsInWorld(string guid)
        {
            int kind = (int)StructureKind.Tomb;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetInt(ZdoKeys.Kind) == kind && zdo.GetString(ZdoKeys.Guid) == guid) return true;
            }
            return false;
        }
    }
}
