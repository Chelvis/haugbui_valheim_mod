#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Diagnostics;
using Haugbui.Integration;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    internal enum TestStatus
    {
        Pass,
        Fail,
        Skip
    }

    internal sealed class TestResult
    {
        public string Suite;
        public string Name;
        public TestStatus Status;
        public string Detail;
    }

    /// <summary>Results, cleanup and world helpers shared by the self-test suites.</summary>
    internal sealed class SelfTestContext
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        public SelfTestContext(string outputFolder)
        {
            OutputFolder = outputFolder;
        }

        public string OutputFolder { get; }
        public string Suite { get; set; } = "";
        public List<TestResult> Results { get; } = new List<TestResult>();
        public int MonstersCleared { get; set; }
        public bool PresenceClear { get; private set; }
        public Player Player => Player.m_localPlayer;

        public bool Check(string name, bool condition, string detail = "")
        {
            Results.Add(new TestResult { Suite = Suite, Name = name, Status = condition ? TestStatus.Pass : TestStatus.Fail, Detail = detail });
            return condition;
        }

        public void Skip(string name, string reason) =>
            Results.Add(new TestResult { Suite = Suite, Name = name, Status = TestStatus.Skip, Detail = reason });

        public void Fail(string name, string detail) => Check(name, false, detail);

        public T Track<T>(T component) where T : Component
        {
            if (component != null) _created.Add(component.gameObject);
            return component;
        }

        public GameObject Track(GameObject go)
        {
            if (go != null) _created.Add(go);
            return go;
        }

        public void Cleanup()
        {
            foreach (GameObject go in _created.Where(g => g != null))
            {
                ZNetView view = go.GetComponent<ZNetView>();
                if (view != null && view.IsValid()) ZNetScene.instance.Destroy(go);
                else UnityEngine.Object.Destroy(go);
            }
            _created.Clear();
        }

        /// <summary>
        /// Drops every Haugbui piece the character's loaded area still holds, so a previous run cannot sit inside the next one.
        /// </summary>
        public IEnumerator ClearPresence()
        {
            Suite = "setup";
            int removed = 0;
            float end = Time.time + 3f;
            while (Time.time < end)
            {
                int batch = SweepPresence();
                removed += batch;
                if (batch == 0) break;
                yield return null;
            }
            yield return null;
            int left = CountPresence();
            PresenceClear = Check("Haugbui structures cleared around the player", left == 0,
                left == 0 ? $"{removed} removed" : $"{removed} removed, {left} left");
            if (removed > 0) Log.Info($"Self-test: removed {removed} Haugbui piece(s) around the player, {left} left.");
        }

        private static int SweepPresence()
        {
            int removed = 0;
            foreach (HaugbuiCreature creature in HaugbuiCreature.All.ToList())
            {
                if (creature == null || !InPresence(creature.transform.position)) continue;
                DestroyOwned(creature.gameObject);
                removed++;
            }
            foreach (HaugbuiStructure structure in StructureRegistry.Loaded.ToList())
            {
                if (structure == null || !InPresence(structure.transform.position)) continue;
                DestroyOwned(structure.gameObject);
                removed++;
            }
            if (ZDOMan.instance == null) return removed;
            List<ZDO> records = ZDOMan.instance.m_objectsByID.Values
                .Where(zdo => zdo.GetInt(ZdoKeys.Kind) != (int)StructureKind.None && InPresence(zdo.GetPosition()))
                .ToList();
            foreach (ZDO zdo in records)
            {
                if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance.DestroyZDO(zdo);
                removed++;
            }
            return removed;
        }

        private static int CountPresence()
        {
            int left = HaugbuiCreature.All.Count(c => c != null && InPresence(c.transform.position));
            left += StructureRegistry.Loaded.Count(s => s != null && InPresence(s.transform.position));
            if (ZDOMan.instance == null) return left;
            left += ZDOMan.instance.m_objectsByID.Values.Count(zdo =>
                zdo.GetInt(ZdoKeys.Kind) != (int)StructureKind.None && InPresence(zdo.GetPosition()));
            return left;
        }

        private static bool InPresence(Vector3 point)
        {
            Player player = Player.m_localPlayer;
            return player != null && ZNet.instance != null && ZNetScene.InActiveArea(point, player.transform.position);
        }

        private static void DestroyOwned(GameObject go)
        {
            if (go == null || ZNetScene.instance == null) return;
            ZNetView view = go.GetComponent<ZNetView>();
            if (view != null && view.IsValid() && !view.IsOwner()) view.ClaimOwnership();
            ZNetScene.instance.Destroy(go);
        }

        public static Vector3 OnGround(Vector3 position)
        {
            position.y = ZoneSystem.instance.GetGroundHeight(position);
            return position;
        }

        /// <summary>Room left around an arena so a chest or a drop placed beside it stays outside the same rules.</summary>
        internal const float FixtureMargin = 48f;

        /// <summary>
        /// A dry, flat, loaded spot far enough from the player that his bed and any Haugbui already working
        /// stay outside the placement rules. He stands still; the fixtures move.
        /// </summary>
        public static Vector3 FindDryArena(Vector3 preferred, float radius = 6f)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return NearestDry(preferred, radius);

            Vector3 origin = player.transform.position;
            Vector3 away = preferred - origin;
            away.y = 0f;
            if (away.sqrMagnitude < 1f) away = player.transform.forward;
            away.y = 0f;
            away.Normalize();

            float beds = Clearance(PlacementCheck.BedDistance);
            float reach = Clearance(ProgressionTable.DefaultRadius[ProgressionTable.DefaultRadius.Length - 1]);
            Vector3 pastTheBed = preferred;
            float pastTheBedDistance = 0f;
            bool haveBed = false;
            for (float dist = beds; dist <= reach + 32f; dist += 8f)
            {
                if (!TryFindDry(origin + away * dist, radius, out Vector3 spot)) continue;
                float distance = Vector3.Distance(origin, spot);
                if (ClearOfPlayer(spot, beds) && distance > pastTheBedDistance)
                {
                    pastTheBed = spot;
                    pastTheBedDistance = distance;
                    haveBed = true;
                }
                if (ClearOfPlayer(spot, reach)) return spot;
            }
            return haveBed ? pastTheBed : NearestDry(preferred, radius);
        }

        private static float Clearance(float rule) => rule + FixtureMargin;

        /// <summary>True when the spot, and the fixtures placed around it, sit outside the player's bed and tombs.</summary>
        private static bool ClearOfPlayer(Vector3 spot, float reach)
        {
            Player player = Player.m_localPlayer;
            if (player != null && Vector3.Distance(player.transform.position, spot) < reach) return false;
            float beds = Clearance(PlacementCheck.BedDistance);
            if (UnityEngine.Object.FindObjectsByType<Bed>(FindObjectsSortMode.None)
                .Any(b => b != null && Vector3.Distance(b.transform.position, spot) < beds)) return false;
            float tombs = Clearance(Mathf.Max(PlacementCheck.TombSpacing, PlacementCheck.SameOwnerSpacing));
            tombs = Mathf.Max(tombs, reach);
            return StructureRegistry.Tombs.All(t => t == null || Vector3.Distance(t.transform.position, spot) >= tombs);
        }

        private static Vector3 NearestDry(Vector3 preferred, float radius)
        {
            return TryFindDry(preferred, radius, out Vector3 spot) ? spot : OnGround(preferred);
        }

        private static bool TryFindDry(Vector3 preferred, float radius, out Vector3 spot)
        {
            float water = ZoneSystem.instance.m_waterLevel + 0.5f;
            for (float ring = 0; ring <= 60f; ring += 6f)
            {
                for (int step = 0; step < (ring == 0 ? 1 : 12); step++)
                {
                    float angle = step * Mathf.PI / 6f;
                    Vector3 candidate = OnGround(preferred + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * ring);
                    if (!LoadedArea.IsLoaded(candidate) || !IsDryAndFlat(candidate, radius, water)) continue;
                    spot = candidate;
                    return true;
                }
            }
            spot = default;
            return false;
        }

        private static bool IsDryAndFlat(Vector3 center, float radius, float water)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                float h = ZoneSystem.instance.GetGroundHeight(center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius);
                min = Mathf.Min(min, h);
                max = Mathf.Max(max, h);
            }
            return min > water && center.y > water && max - min < 1.5f;
        }

        /// <summary>Spawns a vanilla networked prefab (test fixture; no creator, no resources).</summary>
        public GameObject SpawnVanilla(string prefabName, Vector3 position, Quaternion rotation)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (prefab == null) return null;
            return Track(UnityEngine.Object.Instantiate(prefab, position, rotation));
        }

        /// <summary>Destroys only the GameObject, like a zone unload, and waits for ZNetScene to recreate it from its ZDO.</summary>
        public IEnumerator Reload(GameObject go, Action<GameObject> done, float timeout = 10f)
        {
            ZNetView view = go.GetComponent<ZNetView>();
            ZDO zdo = view.GetZDO();
            view.ResetZDO();
            ZNetScene.instance.m_instances.Remove(zdo);
            _created.Remove(go);
            UnityEngine.Object.Destroy(go);
            yield return null;

            float end = Time.time + timeout;
            ZNetView recreated = null;
            while (Time.time < end && (recreated = ZNetScene.instance.FindInstance(zdo)) == null)
                yield return null;

            if (recreated != null)
            {
                _created.Add(recreated.gameObject);
                // Containers read their items from the ZDO on their first CheckForChanges, up to a second later.
                Container container = recreated.GetComponent<Container>();
                while (container != null && Time.time < end && container.m_lastRevision != zdo.DataRevision)
                    yield return null;
            }
            done(recreated != null ? recreated.gameObject : null);
        }

        public bool Tracks(GameObject go) => go != null && _created.Contains(go);

        /// <summary>
        /// Removes hostile creatures near the player (no drops), so a wandering monster cannot hit a Haugbui under test.
        /// Players, tamed creatures, Haugbui test creatures and creatures a suite spawned itself are kept.
        /// </summary>
        public int ClearMonsters(Vector3 center, float radius)
        {
            int removed = 0;
            foreach (Character character in Character.GetAllCharacters().ToList())
            {
                if (character == null || character.IsPlayer() || character.IsTamed() || character.IsDead()) continue;
                if (Tracks(character.gameObject)) continue;
                if (character.GetComponent<MonsterAI>() == null) continue;
                if (character.GetComponent<Creature.HaugbuiCreature>() != null || character.GetComponent<Spikes.FlightSpikeController>() != null) continue;
                if (Vector3.Distance(character.transform.position, center) > radius) continue;
                ZNetView view = character.GetComponent<ZNetView>();
                if (view == null || !view.IsValid() || !view.IsOwner()) continue;
                ZNetScene.instance.Destroy(character.gameObject);
                removed++;
            }
            return removed;
        }

        public static IEnumerator WaitFor(Func<bool> condition, float timeout)
        {
            float end = Time.time + timeout;
            while (Time.time < end && !condition()) yield return null;
        }

        /// <summary>Places a Haugbui piece through the vanilla placement flow (creator, OnPlaced), without resources.</summary>
        public Haugbui.Structures.HaugbuiStructure PlaceStructure(Haugbui.Core.Structures.StructureKind kind, Vector3 position)
        {
            Piece piece = ZNetScene.instance.GetPrefab(Haugbui.Structures.StructureDefinitions.For(kind).PieceId)?.GetComponent<Piece>();
            if (piece == null) return null;
            Player.PlacePiece(piece, position, Quaternion.identity, doAttack: false);
            Haugbui.Structures.HaugbuiStructure placed = Haugbui.Structures.StructureRegistry.Loaded
                .Where(s => s.Kind == kind && Vector3.Distance(s.transform.position, position) < 0.1f)
                .OrderByDescending(s => s.GetInstanceID())
                .FirstOrDefault();
            return placed != null ? Track(placed) : null;
        }

        /// <summary>Places a vanilla piece as the local player built it (creator set), without resources.</summary>
        public GameObject PlaceVanilla(string prefabName, Vector3 position, Quaternion rotation)
        {
            Piece piece = ZNetScene.instance.GetPrefab(prefabName)?.GetComponent<Piece>();
            if (piece == null) return null;
            Player.PlacePiece(piece, position, rotation, doAttack: false);
            Piece placed = UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)
                .Where(p => p != null && Utils.GetPrefabName(p.gameObject) == prefabName && Vector3.Distance(p.transform.position, position) < 0.1f)
                .OrderByDescending(p => p.GetInstanceID())
                .FirstOrDefault();
            return placed != null ? Track(placed.gameObject) : null;
        }

        public static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
        }

        /// <summary>A popup left open blocks movement for every later check. Drain the whole stack.</summary>
        public static void DismissPopups()
        {
            for (int i = 0; i < 5 && UnifiedPopup.IsVisible(); i++) UnifiedPopup.Pop();
        }

        public void WriteReport(TimeSpan duration)
        {
            Directory.CreateDirectory(OutputFolder);
            var sb = new StringBuilder();
            int pass = Results.Count(r => r.Status == TestStatus.Pass);
            int fail = Results.Count(r => r.Status == TestStatus.Fail);
            int skip = Results.Count(r => r.Status == TestStatus.Skip);
            sb.AppendLine($"Haugbui {HaugbuiPlugin.PluginVersion} self-test on Valheim {Version.GetVersionString()} — {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"RESULT: {(fail == 0 ? "PASS" : "FAIL")} — {pass} passed, {fail} failed, {skip} skipped in {duration.TotalSeconds:0.0}s");
            sb.AppendLine($"Hostile creatures removed near the player during the run: {MonstersCleared}");
            sb.AppendLine();
            foreach (IGrouping<string, TestResult> suite in Results.GroupBy(r => r.Suite))
            {
                sb.AppendLine($"[{suite.Key}]");
                foreach (TestResult r in suite)
                    sb.AppendLine($"  {r.Status.ToString().ToUpperInvariant(),-4} {r.Name}{(string.IsNullOrEmpty(r.Detail) ? "" : " — " + r.Detail)}");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(OutputFolder, "report.txt"), sb.ToString());
        }
    }
}
#endif
