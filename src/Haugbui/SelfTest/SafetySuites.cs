#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>HB-T-075: a clean check stays quiet; hiding one tomb ZDO warns; putting it back clears the warning.</summary>
    internal sealed class WorldSuite
    {
        private readonly SelfTestContext _t;

        public WorldSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "World";
            WorldSafety.ResetForTests();
            for (int i = 0; i < 5 && UnifiedPopup.IsVisible(); i++) UnifiedPopup.Pop();

            WorldSafety.PretendNoMarker = true;
            WorldSafety.Check(showPopup: true);
            _t.Check("clean state does not warn", !WorldSafety.WarningShown && !UnifiedPopup.IsVisible());
            WorldSafety.PretendNoMarker = false;

            Player player = _t.Player;
            Vector3 arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 8f);
            GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(arena + new Vector3(-4f, 0f, 0f)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, arena);
            yield return SelfTestContext.WaitFrames(3);
            if (!_t.Check("tomb placed for the world marker", tomb != null)) yield break;

            MarkerReading marker = WorldMarker.Read();
            _t.Check("marker records the tombs", marker.Present && marker.CountReadable && marker.RecordedTombs >= 1,
                marker.Present ? marker.RecordedTombs.ToString() : "absent");

            WorldSafety.ResetForTests();
            WorldSafety.ExcludedTombGuids.Add(tomb.Guid);
            WorldSafety.Check(showPopup: true);
            _t.Check("hiding one tomb ZDO warns", WorldSafety.WarningShown && !UnifiedPopup.IsVisible(),
                $"remembered {WorldMarker.Read().RecordedTombs}, scan {WorldSafety.CountTombs(honorExclusions: true)}");

            WorldSafety.ExcludedTombGuids.Clear();
            WorldSafety.ResetForTests();
            WorldSafety.Check(showPopup: true);
            _t.Check("restoring the tomb clears the warning", !WorldSafety.WarningShown && !UnifiedPopup.IsVisible(),
                $"remembered {WorldMarker.Read().RecordedTombs}, scan {WorldSafety.CountTombs()}");
        }
    }

    /// <summary>HB-T-076: the copy runs only while the option is on, and the test removes the folder it created.</summary>
    internal sealed class BackupSuite
    {
        private readonly SelfTestContext _t;

        public BackupSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Backup";
            string source = WorldSafety.LocalWorldFolder();
            if (string.IsNullOrEmpty(source))
            {
                World world = ZNet.instance != null ? ZNet.instance.GetWorld() : null;
                _t.Fail("backup folder", $"no worlds_local folder for '{ZNet.instance?.GetWorldName()}' (file source {world?.m_fileSource})");
                yield break;
            }

            string parent = Path.GetDirectoryName(source);
            var previous = HaugbuiPlugin.Instance.Settings.BackupWorldOnLoad.Value;
            var created = new List<string>();
            try
            {
                if (!WorldSafety.HasHaugbuiData())
                {
                    Player player = _t.Player;
                    Vector3 arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.right * 8f);
                    GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(arena + new Vector3(-4f, 0f, 0f)), Quaternion.identity);
                    yield return null;
                    if (bed != null) bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
                    HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, arena);
                    yield return SelfTestContext.WaitFrames(3);
                    if (!_t.Check("tomb placed so the world has Haugbui data", tomb != null)) yield break;
                }

                HashSet<string> before = Folders(parent);
                HaugbuiPlugin.Instance.Settings.BackupWorldOnLoad.Value = false;
                WorldSafety.ResetForTests();
                yield return WorldSafety.RunBackup();
                _t.Check("backup off creates no folder", Folders(parent).SetEquals(before));

                HaugbuiPlugin.Instance.Settings.BackupWorldOnLoad.Value = true;
                WorldSafety.ResetForTests();
                yield return WorldSafety.RunBackup();
                created.AddRange(Folders(parent).Except(before));
                bool hasFiles = created.Count == 1 && Directory.Exists(created[0])
                                && Directory.GetFiles(created[0], "*", SearchOption.AllDirectories).Length > 0;
                _t.Check("backup folder appears and contains the world files", hasFiles,
                    created.Count == 0 ? "no folder" : string.Join(", ", created.Select(Path.GetFileName)));
            }
            finally
            {
                HaugbuiPlugin.Instance.Settings.BackupWorldOnLoad.Value = previous;
                WorldSafety.ResetForTests();
                foreach (string folder in created)
                {
                    try
                    {
                        if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    }
                    catch (System.Exception e)
                    {
                        _t.Fail("backup folder removed", e.Message);
                    }
                }
            }
            if (created.Count > 0)
                _t.Check("backup folder removed", created.All(folder => !Directory.Exists(folder)));
        }

        private static HashSet<string> Folders(string parent) =>
            new HashSet<string>(Directory.GetDirectories(parent)
                .Where(path => Path.GetFileName(path).StartsWith(WorldBackup.Prefix, System.StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>HB-T-077: a corrupt tomb blob is kept, and that tomb does not run.</summary>
    internal sealed class IntegritySuite
    {
        private readonly SelfTestContext _t;

        public IntegritySuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Integrity";
            Player player = _t.Player;
            Vector3 arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 10f);
            GameObject bed = _t.SpawnVanilla("bed", SelfTestContext.OnGround(arena + new Vector3(-4f, 0f, 0f)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, arena);
            yield return SelfTestContext.WaitFrames(3);
            if (!_t.Check("tomb placed", tomb != null) || tomb.Zdo == null) yield break;

            int key = ZdoKeys.TombState.GetStableHashCode();
            byte[] corrupt = { 9, 9, 9, 9, 9, 9, 9, 8 };
            tomb.Zdo.Set(key, corrupt);
            GameObject again = null;
            yield return _t.Reload(tomb.gameObject, go => again = go);
            HaugbuiStructure restored = again != null ? again.GetComponent<HaugbuiStructure>() : null;
            byte[] kept = restored != null ? restored.Zdo.GetByteArray(key) : null;
            _t.Check("corrupt blob kept", kept != null && kept.SequenceEqual(corrupt), kept == null ? "missing" : kept.Length + " bytes");
            _t.Check("corrupt tomb does not run", restored != null && restored.BlobsBlocked && restored.TombState == null);
            yield return SelfTestContext.WaitFrames(10);
            _t.Check("load did not throw", restored != null);
        }
    }
}
#endif
