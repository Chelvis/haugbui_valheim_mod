#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Haugbui;
using Haugbui.Brain;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Integration;
using Haugbui.Registry;
using Haugbui.Structures;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>HB-201: one Haugbui planning against 100 containers. Intervals stay as they are unless this misses.</summary>
    internal sealed class PerfSuite
    {
        private const int Chests = 100;
        private const int Samples = 20;
        private const double FramesPerSecond = 60;

        private readonly SelfTestContext _t;
        private readonly FakeTime _time = new FakeTime();
        private Vector3 _arena;
        private TombController _tomb;

        public PerfSuite(SelfTestContext context) => _t = context;

        public IEnumerator Run()
        {
            _t.Suite = "Perf";
            TimeSource.Override = _time;
            try
            {
                yield return Setup();
                if (_tomb == null || _tomb.Creature == null) yield break;
                Measure();
            }
            finally
            {
                TimeSource.Override = null;
            }
        }

        private IEnumerator Setup()
        {
            Player player = _t.Player;
            _arena = SelfTestContext.FindDryArena(player.transform.position + player.transform.forward * 30f, 16f);
            GameObject bed = _t.SpawnVanilla("bed", Ground(new Vector3(-6, 0, -6)), Quaternion.identity);
            if (bed == null)
            {
                _t.Fail("spawn test bed", "prefab 'bed' not found");
                yield break;
            }
            yield return null;
            bed.GetComponent<Bed>().SetOwner(player.GetPlayerID(), player.GetPlayerName());
            // A tomb placed during the night waits for the next dusk, so it is built by day and then sent into the night.
            _time.Fraction = FakeTime.Noon;
            HaugbuiStructure tomb = _t.PlaceStructure(StructureKind.Tomb, _arena);
            yield return SelfTestContext.WaitFrames(3);
            _tomb = tomb != null ? tomb.GetComponent<TombController>() : null;
            if (!_t.Check("tomb placed", _tomb != null)) yield break;
            _tomb.SetToggle(TombToggles.GroundItems, false, out _);
            _tomb.SetToggle(TombToggles.Harvesting, false, out _);
            _tomb.SetToggle(TombToggles.Production, false, out _);
            _time.Fraction = FakeTime.Night;
            _tomb.TickNow();
            yield return SelfTestContext.WaitFor(() => _tomb.Creature != null && _tomb.Machine.State == HaugbuiState.Idle, 8f);
            if (!_t.Check("Haugbui out", _tomb.Creature != null, _tomb.Machine.State.ToString())) yield break;

            int placed = 0;
            for (int i = 0; i < Chests; i++)
            {
                int x = i % 10, z = i / 10;
                Vector3 offset = new Vector3((x - 4.5f) * 3f, 0f, (z - 4.5f) * 3f + 8f);
                if (_t.PlaceVanilla("piece_chest_wood", Ground(offset), Quaternion.identity) != null) placed++;
                if (i % 10 == 9) yield return null;
            }
            var found = new List<Container>();
            WorldRegistry.QueryContainers(_arena, _tomb.Attributes.Radius, found);
            _t.Check("100 containers in range", placed == Chests && found.Count >= Chests, $"placed {placed}, indexed {found.Count}");
        }

        private void Measure()
        {
            HaugbuiWorker worker = _tomb.Worker;
            if (worker.Plan != null) worker.Abort("perf warmup");
            worker.Tick();
            if (worker.Plan != null) worker.Abort("perf warmup");

            double tickMs = 0, stepMs = 0;
            for (int i = 0; i < Samples; i++)
            {
                var tick = Stopwatch.StartNew();
                worker.Tick();
                tick.Stop();
                tickMs += tick.Elapsed.TotalMilliseconds;
                if (worker.Plan != null) worker.Abort("perf sample");

                var step = Stopwatch.StartNew();
                worker.Step();
                step.Stop();
                stepMs += step.Elapsed.TotalMilliseconds;
            }
            tickMs /= Samples;
            stepMs /= Samples;
            double interval = Math.Max(0.05, HaugbuiPlugin.Instance.Settings.PlanningIntervalSeconds.Value);
            double average = stepMs + tickMs / (interval * FramesPerSecond);
            _t.Check("average per frame stays within 0.3 ms", average <= 0.3,
                $"plan {tickMs:0.000} ms every {interval:0.##}s, step {stepMs:0.000} ms, average {average:0.000} ms at {FramesPerSecond:0} fps");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            long before = GC.GetTotalMemory(true);
            worker.Tick();
            long delta = GC.GetTotalMemory(false) - before;
            if (worker.Plan != null) worker.Abort("perf sample");
            _t.Check("planning allocates at most 1 KB", delta <= 1024, $"{delta} bytes");
        }

        private Vector3 Ground(Vector3 offset) => SelfTestContext.OnGround(_arena + offset);
    }
}
#endif
