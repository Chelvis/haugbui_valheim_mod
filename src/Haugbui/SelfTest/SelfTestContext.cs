#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        public static Vector3 OnGround(Vector3 position)
        {
            position.y = ZoneSystem.instance.GetGroundHeight(position);
            return position;
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

            if (recreated != null) _created.Add(recreated.gameObject);
            done(recreated != null ? recreated.gameObject : null);
        }

        public static IEnumerator WaitFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
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
