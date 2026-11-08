using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.GameApi;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>
    /// Tombs that opted in stay simulated while their Haugbui may be out: terrain TTL is refreshed and objects stay
    /// in the same lists the game uses for the player. Rest, a revive wait, and daytime without the Altar do not pin.
    /// </summary>
    internal static class KeepLoaded
    {
        private static readonly List<Vector3> Anchors = new List<Vector3>();
        private static readonly List<ZDOID> Kept = new List<ZDOID>();
        private static readonly HashSet<string> FullTimeTombs = new HashSet<string>();
        private static readonly List<ZDO> Near = new List<ZDO>();
        private static readonly List<ZDO> Distant = new List<ZDO>();
        private static readonly List<ZDO> Spawn = new List<ZDO>();
        private static readonly List<ZDO> Empty = new List<ZDO>();

        private static float _scanAt = -1f;
        private static int _listedFrame = -1;

        internal static bool Active =>
            GameApiSelfCheck.IsAvailable(Features.RemoteWork)
            && HaugbuiPlugin.Instance != null
            && HaugbuiPlugin.Instance.Settings.AwayModeEnabled.Value;

        internal static void Stamp(ZDO zdo, bool on)
        {
            if (zdo == null) return;
            zdo.Set(ZdoKeys.KeepLoaded, on ? 1 : 0);
            _scanAt = -1f;
        }

        internal static void Invalidate() => _scanAt = -1f;

        /// <summary>True when this point sits in an opted-in tomb's simulation bubble. Uses the last anchor list.</summary>
        internal static bool Inside(Vector3 point)
        {
            if (!Active) return false;
            for (int i = 0; i < Anchors.Count; i++)
                if (ZNetScene.InActiveArea(point, Anchors[i])) return true;
            return false;
        }

        internal static bool Anchored(Vector3 point)
        {
            if (!Active || ZDOMan.instance == null) return false;
            RefreshAnchors(true);
            for (int i = 0; i < Anchors.Count; i++)
                if ((Anchors[i] - point).sqrMagnitude < 4f) return true;
            return false;
        }

        internal static void Retain(List<ZDO> keep)
        {
            if (!ListObjects()) return;
            keep.AddRange(Near);
            keep.AddRange(Distant);
        }

        internal static void SpawnMissing(ZNetScene scene)
        {
            if (scene == null || !ListObjects()) return;
            Spawn.Clear();
            for (int i = 0; i < Near.Count; i++)
                if (Near[i] != null && !Near[i].Created) Spawn.Add(Near[i]);
            if (Spawn.Count == 0) return;
            scene.CreateObjects(Spawn, Empty);
        }

        internal static void Poke(ZoneSystem zones)
        {
            if (zones == null || !Refresh()) return;
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            int near = distance.NearSimulationDistance;
            bool spawned = false;
            for (int a = 0; a < Anchors.Count; a++)
            {
                Vector2s center = ZoneSystem.GetZone(Anchors[a]);
                for (int y = center.y - near; y <= center.y + near; y++)
                {
                    for (int x = center.x - near; x <= center.x + near; x++)
                    {
                        var id = new Vector2s(x, y);
                        if (!distance.IsClassic && !zones.ZonesWithinRadius(center, id, near)) continue;
                        if (!zones.IsZoneLoaded(id))
                        {
                            // One new zone per tick, matching CreateLocalZones, so a far login does not hitch.
                            if (spawned) continue;
                            if (zones.PokeLocalZone(id)) spawned = true;
                            continue;
                        }
                        zones.PokeLocalZone(id);
                    }
                }
            }
        }

        private static bool ListObjects()
        {
            if (!Refresh()) return false;
            if (_listedFrame == Time.frameCount) return Near.Count > 0 || Distant.Count > 0;
            Near.Clear();
            Distant.Clear();
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            for (int i = 0; i < Anchors.Count; i++)
                ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(Anchors[i]), distance, Near, Distant);
            _listedFrame = Time.frameCount;
            return true;
        }

        private static bool Refresh()
        {
            if (!Active || ZNet.instance == null || ZoneSystem.instance == null || ZDOMan.instance == null) return false;
            RefreshAnchors(false);
            return Anchors.Count > 0;
        }

        private static void RefreshAnchors(bool forceScan)
        {
            Anchors.Clear();
            TombController.CollectAwayAnchors(Anchors);
            if (forceScan || Time.time >= _scanAt)
            {
                _scanAt = Time.time + 2f;
                ScanKept();
            }
            for (int i = 0; i < Kept.Count; i++)
            {
                ZDO zdo = ZDOMan.instance.GetZDO(Kept[i]);
                if (zdo == null || !HoldsFromSave(zdo)) continue;
                Vector3 pos = zdo.GetPosition();
                bool have = false;
                for (int j = 0; j < Anchors.Count; j++)
                    if ((Anchors[j] - pos).sqrMagnitude < 4f) { have = true; break; }
                if (!have) Anchors.Add(pos);
            }
        }

        private static void ScanKept()
        {
            Kept.Clear();
            FullTimeTombs.Clear();
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo == null) continue;
                if (zdo.GetInt(ZdoKeys.KeepLoaded) != 0) Kept.Add(zdo.m_uid);
            }
            float range = StructureRegistry.UpgradeRange;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo == null || zdo.GetInt(ZdoKeys.Kind) != (int)StructureKind.Altar) continue;
                string bound = zdo.GetString(ZdoKeys.TombGuid);
                if (string.IsNullOrEmpty(bound)) continue;
                Vector3 altar = zdo.GetPosition();
                for (int i = 0; i < Kept.Count; i++)
                {
                    ZDO tomb = ZDOMan.instance.GetZDO(Kept[i]);
                    if (tomb == null || tomb.GetString(ZdoKeys.Guid) != bound) continue;
                    if (Vector3.Distance(altar, tomb.GetPosition()) <= range) FullTimeTombs.Add(bound);
                }
            }
        }

        /// <summary>
        /// A far tomb has no controller, so whether he may come out is read from the save. Without that, dusk would
        /// never load the area and he would stay inside until the player walked back.
        /// </summary>
        private static bool HoldsFromSave(ZDO zdo)
        {
            if (HaugbuiPlugin.Instance == null) return false;
            if (TombStateSerializer.Instance.TryDeserialize(new ZdoBlobStore(zdo).Get(ZdoKeys.TombState), out TombState state) != BlobReadStatus.Ok
                || state == null || !state.Has(TombToggles.AwayMode)) return false;
            ITimeSource time = TimeSource.Current;
            return PresencePolicy.ShouldBeOut(new PresenceInput
            {
                Now = time.Now,
                DayLengthSeconds = time.DayLengthSeconds,
                FullTime = FullTimeTombs.Contains(state.Guid),
                Resting = state.Has(TombToggles.Resting),
                Dead = state.CreatureState == (int)HaugbuiState.Dead,
                DeathTime = state.DeathTime,
                CooldownSeconds = TombController.CooldownSeconds,
                DawnReturnLeadSeconds = HaugbuiPlugin.Instance.Settings.DawnReturnLeadSeconds.Value,
                StartTime = state.StartTime,
            });
        }
    }
}
