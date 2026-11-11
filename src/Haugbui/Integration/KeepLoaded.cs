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
        private static readonly List<ZDO> Altars = new List<ZDO>();
        private static readonly HashSet<Vector2s> UnappliedTerrain = new HashSet<Vector2s>();

        private static ZDOMan _scannedWorld;
        private static bool _scanDone;
        private static int _listedFrame = -1;

        internal static bool Active =>
            GameApiSelfCheck.IsAvailable(Features.RemoteWork)
            && HaugbuiPlugin.Instance != null
            && HaugbuiPlugin.Instance.Settings.AwayModeEnabled.Value;

        internal static void Stamp(ZDO zdo, bool on)
        {
            if (zdo == null) return;
            zdo.Set(ZdoKeys.KeepLoaded, on ? 1 : 0);
            if (on)
            {
                if (!Kept.Contains(zdo.m_uid)) Kept.Add(zdo.m_uid);
            }
            else
                Kept.Remove(zdo.m_uid);
        }

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
            RefreshAnchors();
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
            MarkUnappliedTerrain();
            Spawn.Clear();
            for (int i = 0; i < Near.Count; i++)
            {
                ZDO zdo = Near[i];
                if (zdo == null || zdo.Created || !CanSpawn(zdo)) continue;
                Spawn.Add(zdo);
            }
            if (Spawn.Count == 0) return;
            scene.CreateObjects(Spawn, Empty);
        }

        internal static void Poke(ZoneSystem zones)
        {
            if (zones == null || !Refresh()) return;
            SimulationDistance distance = ZNet.instance.GetSyncedSimulationDistance();
            int near = distance.NearSimulationDistance;
            for (int a = 0; a < Anchors.Count; a++)
            {
                Vector2s center = ZoneSystem.GetZone(Anchors[a]);
                for (int y = center.y - near; y <= center.y + near; y++)
                {
                    for (int x = center.x - near; x <= center.x + near; x++)
                    {
                        var id = new Vector2s(x, y);
                        if (!distance.IsClassic && !zones.ZonesWithinRadius(center, id, near)) continue;
                        // SpawnZone builds the heightmap before the saved TerrainComp exists. Pieces
                        // then support-check that fresh ground. Only refresh a zone the player already loaded.
                        if (zones.IsZoneLoaded(id)) zones.PokeLocalZone(id);
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
            RefreshAnchors();
            return Anchors.Count > 0;
        }

        private static void RefreshAnchors()
        {
            Anchors.Clear();
            TombController.CollectAwayAnchors(Anchors);
            EnsureScanned();
            TombController.NoteAwayAltars(FullTimeTombs);
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

        /// <summary>
        /// One walk of the world per load. Repeating it hitches: every ZDO is visited, and the keep flag only
        /// changes through Stamp. A live tomb reports its Altar afterwards, so a later build does not need another walk.
        /// </summary>
        private static void EnsureScanned()
        {
            ZDOMan world = ZDOMan.instance;
            if (world == null) return;
            if (world != _scannedWorld)
            {
                _scannedWorld = world;
                _scanDone = false;
                Kept.Clear();
                FullTimeTombs.Clear();
            }
            // The player exists only after the world has loaded its ZDOs.
            if (_scanDone || Player.m_localPlayer == null || world.m_objectsByID.Count == 0) return;
            _scanDone = true;
            ScanKept(world);
        }

        private static void ScanKept(ZDOMan world)
        {
            Kept.Clear();
            Altars.Clear();
            foreach (ZDO zdo in world.m_objectsByID.Values)
            {
                if (zdo == null) continue;
                if (zdo.GetInt(ZdoKeys.KeepLoaded) != 0) Kept.Add(zdo.m_uid);
                else if (zdo.GetInt(ZdoKeys.Kind) == (int)StructureKind.Altar) Altars.Add(zdo);
            }
            float range = StructureRegistry.UpgradeRange;
            for (int a = 0; a < Altars.Count; a++)
            {
                string bound = Altars[a].GetString(ZdoKeys.TombGuid);
                if (string.IsNullOrEmpty(bound)) continue;
                Vector3 altar = Altars[a].GetPosition();
                for (int i = 0; i < Kept.Count; i++)
                {
                    ZDO tomb = world.GetZDO(Kept[i]);
                    if (tomb == null || tomb.GetString(ZdoKeys.Guid) != bound) continue;
                    if (Vector3.Distance(altar, tomb.GetPosition()) <= range) FullTimeTombs.Add(bound);
                }
            }
            Altars.Clear();
        }

        /// <summary>
        /// A saved ground edit lives on a TerrainComp. The heightmap applies it only after that object exists.
        /// Spawning a piece before then support-checks the fresh ground.
        /// </summary>
        private static void MarkUnappliedTerrain()
        {
            UnappliedTerrain.Clear();
            MarkUnapplied(Near);
            MarkUnapplied(Distant);
        }

        private static void MarkUnapplied(List<ZDO> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ZDO zdo = list[i];
                if (zdo == null) continue;
                byte[] data = zdo.GetByteArray(ZDOVars.s_TCData);
                if (data == null || data.Length == 0) continue;
                if (TerrainComp.FindTerrainCompiler(zdo.GetPosition()) != null) continue;
                UnappliedTerrain.Add(ZoneSystem.GetZone(zdo.GetPosition()));
            }
        }

        private static bool CanSpawn(ZDO zdo)
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.IsZoneLoaded(zdo.GetPosition())) return false;
            return !UnappliedTerrain.Contains(ZoneSystem.GetZone(zdo.GetPosition()));
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
