using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Haugbui.Core.GameApi;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Diagnostics;
using Haugbui.Structures;
using HarmonyLib;
using UnityEngine;

namespace Haugbui.Integration
{
    /// <summary>On load: compare the world marker with a full ZDO scan, then optionally copy the world folder.</summary>
    internal static class WorldSafety
    {
        private static bool _backupDone;
        private static bool _popupShown;

#if DEBUG
        internal static readonly HashSet<string> ExcludedTombGuids = new HashSet<string>();
        internal static bool PretendNoMarker { get; set; }

        /// <summary>Self-test records the verdict without putting a modal on screen.</summary>
        internal static bool SuppressPopup { get; set; }

        internal static bool WarningShown { get; private set; }

        internal static void ResetForTests()
        {
            _backupDone = false;
            _popupShown = false;
            WarningShown = false;
            ExcludedTombGuids.Clear();
            PretendNoMarker = false;
        }
#endif

        public static int CountTombs(string ignoreGuid = null, bool honorExclusions = false)
        {
            if (ZDOMan.instance == null) return 0;
            int kind = (int)StructureKind.Tomb;
            int count = 0;
            foreach (ZDO zdo in ZDOMan.instance.m_objectsByID.Values)
            {
                if (zdo.GetInt(ZdoKeys.Kind) != kind) continue;
                string guid = zdo.GetString(ZdoKeys.Guid) ?? string.Empty;
                if (ignoreGuid != null && guid == ignoreGuid) continue;
#if DEBUG
                if (honorExclusions && ExcludedTombGuids.Contains(guid)) continue;
#endif
                count++;
            }
            return count;
        }

        /// <summary>The on-disk world folder under worlds_local, even when the world's file-source flag is not exactly Local.</summary>
        public static string LocalWorldFolder()
        {
            if (ZNet.instance == null) return null;
            string name = ZNet.instance.GetWorldName();
            if (string.IsNullOrEmpty(name)) return null;
            string path = World.GetSaveDirectory(FileHelpers.FileSource.Local, name);
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Directory.Exists(path) ? path : null;
        }

        /// <param name="ignoreGuid">A tomb the mod just removed; its ZDO may still be in the list for this call.</param>
        public static void RecordLive(string ignoreGuid = null)
        {
            try
            {
                if (!WorldMarker.Available || ZDOMan.instance == null) return;
                if (WorldMarker.Read().FutureSchema) return;
                // ignoreGuid covers a ZDO that is still listed while its destroy callback runs.
                WorldMarker.Write(CountTombs(ignoreGuid));
            }
            catch (Exception e)
            {
                Log.Error("World marker update failed: " + e.Message);
            }
        }

        public static bool HasHaugbuiData()
        {
            MarkerReading marker = WorldMarker.Read();
            return WorldGuard.HasData(marker.Present, CountTombs(), WorldMarker.HasServantSlots());
        }

        public static void Check(bool showPopup)
        {
            try
            {
                if (!WorldMarker.Available || ZDOMan.instance == null) return;
                // Every chunk is loaded into m_objectsByID (ZDOMan.LoadChunks), so a distant tomb is still counted.
                int found = CountTombs(honorExclusions: true);
                bool slots = WorldMarker.HasServantSlots();
                MarkerReading marker = WorldMarker.Read();
#if DEBUG
                if (PretendNoMarker) marker = new MarkerReading(false, 0, false, 0);
#endif
                WorldGuardVerdict verdict = WorldGuard.Compare(marker, found, slots);
#if DEBUG
                WarningShown = verdict == WorldGuardVerdict.Missing;
#endif
                Log.Info($"World guard: {verdict}, marker {(marker.Present ? marker.RecordedTombs.ToString() : "absent")}, scan {found}.");
                if (verdict == WorldGuardVerdict.Missing && showPopup) ShowPopup(marker.RecordedTombs, found);
#if DEBUG
                if (PretendNoMarker) return;
#endif
                if (WorldGuard.ShouldWrite(marker, verdict, found)) WorldMarker.Write(found);
            }
            catch (Exception e)
            {
                Log.Error("World guard failed: " + e.Message);
            }
        }

        private static void ShowPopup(int recorded, int found)
        {
#if DEBUG
            if (SuppressPopup)
            {
                Log.Warning($"World guard: remembered {recorded} tombs, scan found {found}.");
                return;
            }
#endif
            if (_popupShown || Player.m_localPlayer == null || !UnifiedPopup.IsAvailable()) return;
            _popupShown = true;
            string text = Localization.instance.Localize("$haugbui_world_guard_text", recorded.ToString(), found.ToString());
            UnifiedPopup.Push(new WarningPopup("$haugbui_world_guard_title", text, UnifiedPopup.Pop));
            Log.Warning($"World guard: remembered {recorded} tombs, scan found {found}.");
        }

        public static IEnumerator RunBackup()
        {
            if (_backupDone || HaugbuiPlugin.Instance == null) yield break;
            if (!GameApiSelfCheck.IsAvailable(Features.WorldSafety))
            {
                Log.Error("World backup disabled: required game members are missing.");
                yield break;
            }
            bool enabled = HaugbuiPlugin.Instance.Settings.BackupWorldOnLoad.Value;
            bool hasData = false;
            bool local = false;
            string source = null;
            try
            {
                hasData = HasHaugbuiData();
                source = LocalWorldFolder();
                local = source != null;
                if (!local)
                {
                    World world = ZNet.instance != null ? ZNet.instance.GetWorld() : null;
                    Log.Info($"World backup: no local folder for '{ZNet.instance?.GetWorldName()}' (file source {world?.m_fileSource}).");
                }
            }
            catch (Exception e)
            {
                Log.Error("World backup could not read the save path: " + e.Message);
                yield break;
            }

            BackupDecision decision = WorldBackup.Choose(enabled, hasData, local, ZNet.instance != null && ZNet.instance.IsSaving());
            if (decision == BackupDecision.Saving)
            {
                float wait = Time.realtimeSinceStartup + 30f;
                while (Time.realtimeSinceStartup < wait && ZNet.instance != null && ZNet.instance.IsSaving()) yield return null;
                decision = WorldBackup.Choose(enabled, hasData, local, ZNet.instance != null && ZNet.instance.IsSaving());
            }
            if (decision != BackupDecision.Copy)
            {
                if (decision == BackupDecision.NotLocal) Log.Info("World backup skipped: the world is not a local folder.");
                yield break;
            }

            string parent = Path.GetDirectoryName(source);
            string dest = Path.Combine(parent, WorldBackup.FolderName(ZNet.instance.GetWorldName(), DateTime.Now));
            if (Directory.Exists(dest)) dest += "-2";
            bool stop = false;
            Task<bool> copy;
            try
            {
                copy = Task.Run(() => WorldBackup.CopyTree(source, dest, () => stop));
            }
            catch (Exception e)
            {
                Log.Error("World backup failed to start: " + e.Message);
                yield break;
            }
            while (!copy.IsCompleted)
            {
                if (ZNet.instance != null && ZNet.instance.IsSaving()) stop = true;
                yield return null;
            }
            _backupDone = true;
            bool ok = false;
            try
            {
                ok = copy.Status == TaskStatus.RanToCompletion && copy.Result && !stop && (ZNet.instance == null || !ZNet.instance.IsSaving());
            }
            catch (Exception e)
            {
                Log.Error("World backup failed: " + e.Message);
            }
            if (!ok)
            {
                TryDelete(dest);
                Log.Warning("World backup discarded because a save was writing or the copy failed.");
                _backupDone = false;
                yield break;
            }
            Log.Info("World backup copied to " + dest);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception e)
            {
                Log.Warning("World backup folder could not be removed: " + e.Message);
            }
        }
    }

    [HarmonyPatch]
    internal static class WorldSafetyPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy), typeof(GameObject))]
        private static void TombRemoved(GameObject go)
        {
            // The ZDO is already gone. OnDestroy runs next frame; a save before that would remember a tomb that is not there.
            HaugbuiStructure structure = go != null ? go.GetComponent<HaugbuiStructure>() : null;
            if (structure != null && structure.Kind == StructureKind.Tomb) WorldSafety.RecordLive();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
        private static void WorldLoaded()
        {
            if (HaugbuiPlugin.Instance == null) return;
            HaugbuiPlugin.Instance.StartCoroutine(AfterLoad());
        }

        private static IEnumerator AfterLoad()
        {
            float end = Time.time + 30f;
            while (Time.time < end && (Player.m_localPlayer == null || ZoneSystem.instance == null || ZDOMan.instance == null))
                yield return null;
            if (Player.m_localPlayer == null) yield break;
            WorldSafety.Check(showPopup: true);
            Coordination.Reservations.Clear();
            Log.Info("Reservations cleared on load; they are not saved.");
            yield return WorldSafety.RunBackup();
        }
    }
}
