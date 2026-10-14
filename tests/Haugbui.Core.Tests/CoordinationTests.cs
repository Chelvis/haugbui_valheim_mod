using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Coordination;
using Haugbui.Core.Geometry;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class ReservationTableTests
    {
        private double _now;
        private ReservationTable Table() => new ReservationTable(() => _now, 30);

        [SetUp]
        public void Reset() => _now = 1000;

        [Test]
        public void Two_holders_never_hold_the_same_key()
        {
            ReservationTable table = Table();
            Assert.That(table.TryReserve("drop:1", "tombA"), Is.True);
            Assert.That(table.TryReserve("drop:1", "tombB"), Is.False);
            Assert.That(table.TryReserve("drop:1", "tombA"), Is.True, "re-reserving renews");
            Assert.That(table.HolderOf("drop:1"), Is.EqualTo("tombA"));
        }

        [Test]
        public void Reserve_all_is_atomic()
        {
            ReservationTable table = Table();
            table.TryReserve("b", "tombB");
            Assert.That(table.TryReserveAll(new[] { "a", "b" }, "tombA"), Is.False);
            Assert.That(table.HolderOf("a"), Is.Null);
        }

        [Test]
        public void Leases_expire_when_not_renewed()
        {
            ReservationTable table = Table();
            table.TryReserve("pick:7", "tombA");
            _now += 29;
            table.Renew("tombA");
            _now += 29;
            Assert.That(table.TryReserve("pick:7", "tombB"), Is.False, "renewed lease still valid");
            _now += 2;
            Assert.That(table.TryReserve("pick:7", "tombB"), Is.True, "a dead Haugbui's lease expires");
        }

        [Test]
        public void Release_and_release_all()
        {
            ReservationTable table = Table();
            table.TryReserveAll(new[] { "a", "b", "c" }, "tombA");
            table.TryReserve("d", "tombB");
            table.Release("a", "tombB");
            Assert.That(table.HolderOf("a"), Is.EqualTo("tombA"), "only the holder releases");
            Assert.That(table.ReleaseAll("tombA"), Is.EqualTo(3));
            Assert.That(table.Count, Is.EqualTo(1));
            table.Clear();
            Assert.That(table.Count, Is.EqualTo(0));
        }
    }

    public class RetryBackoffTests
    {
        [Test]
        public void Doubles_up_to_the_maximum()
        {
            var backoff = new RetryBackoff(2, 60);
            double now = 0;
            var delays = new List<double>();
            for (int i = 0; i < 8; i++)
            {
                backoff.Fail("Wood", now);
                delays.Add(backoff.DelayOf("Wood"));
                now = backoff.NextTry("Wood");
            }
            Assert.That(delays, Is.EqualTo(new double[] { 2, 4, 8, 16, 32, 60, 60, 60 }));
        }

        [Test]
        public void Failures_while_waiting_do_not_change_the_schedule()
        {
            var backoff = new RetryBackoff(2, 60);
            backoff.Fail("Wood", 0);
            backoff.Fail("Wood", 1);
            Assert.That(backoff.NextTry("Wood"), Is.EqualTo(2));
            Assert.That(backoff.IsWaiting("Wood", 1.5), Is.True);
            Assert.That(backoff.IsWaiting("Wood", 2), Is.False);
        }

        [Test]
        public void Success_forgets_the_key()
        {
            var backoff = new RetryBackoff(2, 60);
            backoff.Fail("Wood", 0);
            backoff.Succeed("Wood");
            Assert.That(backoff.IsWaiting("Wood", 0.5), Is.False);
            backoff.Fail("Wood", 1);
            Assert.That(backoff.DelayOf("Wood"), Is.EqualTo(2));
        }
    }

    public class TaskPlannerTests
    {
        private sealed class FixedProvider : ITaskProvider<object>
        {
            public bool Enabled = true;
            public List<TaskCandidate> Candidates = new List<TaskCandidate>();
            public bool IsEnabled(object context) => Enabled;
            public void Collect(object context, List<TaskCandidate> output) => output.AddRange(Candidates);
        }

        private static TaskCandidate Candidate(string kind, int priority, float distance, params string[] keys) => new TaskCandidate
        {
            Kind = kind, Priority = priority, Distance = distance, Keys = keys,
            Build = () => new TaskPlan(kind, new[] { new TaskStep(StepKind.Take, kind, null, 1) })
        };

        [Test]
        public void Highest_priority_then_nearest()
        {
            var provider = new FixedProvider();
            provider.Candidates.Add(Candidate("low", 1, 0));
            provider.Candidates.Add(Candidate("far", 5, 20));
            provider.Candidates.Add(Candidate("near", 5, 2));
            var planner = new TaskPlanner<object>(new ReservationTable(() => 0), new[] { provider });
            Assert.That(planner.Next(null, "tombA").Kind, Is.EqualTo("near"));
        }

        [Test]
        public void Skips_candidates_reserved_by_another_tomb_and_releases_on_finish()
        {
            var reservations = new ReservationTable(() => 0);
            var provider = new FixedProvider();
            provider.Candidates.Add(Candidate("drop1", 5, 1, "drop:1"));
            provider.Candidates.Add(Candidate("drop2", 5, 2, "drop:2"));
            var planner = new TaskPlanner<object>(reservations, new[] { provider });

            TaskPlan a = planner.Next(null, "tombA");
            TaskPlan b = planner.Next(null, "tombB");
            Assert.That(a.Kind, Is.EqualTo("drop1"));
            Assert.That(b.Kind, Is.EqualTo("drop2"), "never the same reserved key");
            Assert.That(planner.Next(null, "tombC"), Is.Null);

            planner.Finish(a, "tombA");
            Assert.That(planner.Next(null, "tombC").Kind, Is.EqualTo("drop1"));
        }

        [Test]
        public void Empty_build_releases_its_keys()
        {
            var reservations = new ReservationTable(() => 0);
            var provider = new FixedProvider();
            provider.Candidates.Add(new TaskCandidate { Kind = "nothing", Priority = 9, Keys = new[] { "k" }, Build = () => null });
            var planner = new TaskPlanner<object>(reservations, new[] { provider });
            Assert.That(planner.Next(null, "tombA"), Is.Null);
            Assert.That(reservations.HolderOf("k"), Is.Null);
        }

        [Test]
        public void Disabled_providers_are_ignored()
        {
            var provider = new FixedProvider { Enabled = false };
            provider.Candidates.Add(Candidate("x", 1, 0));
            Assert.That(new TaskPlanner<object>(new ReservationTable(() => 0), new[] { provider }).Next(null, "t"), Is.Null);
        }

        [Test]
        public void Plan_advances_through_its_steps()
        {
            var plan = new TaskPlan("x", new[] { new TaskStep(StepKind.Take, "a", null, 1), new TaskStep(StepKind.Put, "b", null, 1) });
            Assert.That(plan.Current.Target, Is.EqualTo("a"));
            plan.Advance();
            Assert.That(plan.Current.Target, Is.EqualTo("b"));
            plan.Advance();
            Assert.That(plan.IsDone && plan.Current == null);
        }
    }

    public class SpatialHashTests
    {
        private sealed class Obj
        {
            public Vec3 P;
        }

        [Test]
        public void Query_matches_a_brute_force_scan()
        {
            var random = new Random(7);
            var hash = new SpatialHash<Obj>(16f);
            var all = new List<Obj>();
            for (int i = 0; i < 2000; i++)
            {
                var o = new Obj { P = new Vec3((float)(random.NextDouble() * 400 - 200), (float)(random.NextDouble() * 40), (float)(random.NextDouble() * 400 - 200)) };
                all.Add(o);
                hash.Add(o, o.P);
            }
            var results = new List<Obj>();
            for (int q = 0; q < 200; q++)
            {
                var center = new Vec3((float)(random.NextDouble() * 400 - 200), 10, (float)(random.NextDouble() * 400 - 200));
                float radius = (float)(random.NextDouble() * 100);
                results.Clear();
                hash.Query(center, radius, results);
                var expected = all.Where(o => Vec3.Distance(o.P, center) <= radius).ToList();
                Assert.That(results, Is.EquivalentTo(expected));
            }
        }

        [Test]
        public void Query_does_not_allocate()
        {
            var hash = new SpatialHash<Obj>(16f);
            for (int i = 0; i < 500; i++)
            {
                var o = new Obj { P = new Vec3(i % 50 * 3f, 0, i / 50 * 3f) };
                hash.Add(o, o.P);
            }
            var results = new List<Obj>(1000);
            hash.Query(new Vec3(60, 0, 15), 50, results);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                results.Clear();
                hash.Query(new Vec3(60, 0, 15), 50, results);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
            Assert.That(results, Is.Not.Empty);
        }

        [Test]
        public void Remove_move_and_prune()
        {
            var hash = new SpatialHash<Obj>(16f);
            var a = new Obj { P = new Vec3(1, 0, 1) };
            var b = new Obj { P = new Vec3(-40, 0, 3) };
            hash.Add(a, a.P);
            hash.Add(b, b.P);
            hash.Add(a, new Vec3(100, 0, 100));
            var results = new List<Obj>();
            hash.Query(Vec3.Zero, 10, results);
            Assert.That(results, Is.Empty, "moved away");
            Assert.That(hash.Remove(b), Is.True);
            Assert.That(hash.Remove(b), Is.False);
            Assert.That(hash.Prune(o => o != a), Is.EqualTo(1));
            Assert.That(hash.Count, Is.EqualTo(0));
        }

        [Test]
        public void Negative_coordinates_land_in_their_own_cells()
        {
            var hash = new SpatialHash<Obj>(16f);
            var o = new Obj { P = new Vec3(-0.5f, 0, -0.5f) };
            hash.Add(o, o.P);
            var results = new List<Obj>();
            hash.Query(new Vec3(0.2f, 0, 0.2f), 1f, results);
            Assert.That(results, Has.Count.EqualTo(1));
        }

        [Test]
        public void Loaded_coverage()
        {
            Assert.That(LoadedCoverage.Compute(Vec3.Zero, 50, 8, _ => true), Is.EqualTo(1f));
            Assert.That(LoadedCoverage.Compute(Vec3.Zero, 50, 8, _ => false), Is.EqualTo(0f));
            float half = LoadedCoverage.Compute(Vec3.Zero, 50, 4, p => p.X < 0);
            Assert.That(half, Is.InRange(0.4f, 0.5f));
        }
    }
}
