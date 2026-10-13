using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Scheduling;
using NUnit.Framework;

namespace Haugbui.Core.Tests
{
    public class TickSchedulerTests
    {
        private const double Frame = 1.0 / 60;

        private static Dictionary<string, List<int>> Simulate(TickScheduler scheduler, double seconds, params string[] names)
        {
            var framesByTick = names.ToDictionary(n => n, _ => new List<int>());
            int frame = 0;
            foreach (string name in names)
            {
                string captured = name;
                scheduler.Register(captured, 1.0, () => framesByTick[captured].Add(frame), now: 0);
            }

            for (double t = 0; t < seconds; t += Frame, frame++)
                scheduler.Update(t);
            return framesByTick;
        }

        [Test]
        public void Ticks_run_at_their_interval()
        {
            var scheduler = new TickScheduler(seed: 1);
            Dictionary<string, List<int>> runs = Simulate(scheduler, 10.0, "a");

            Assert.That(runs["a"].Count, Is.InRange(9, 11));
        }

        [Test]
        public void Two_tombs_never_tick_in_the_same_frame()
        {
            var scheduler = new TickScheduler(seed: 7);
            Dictionary<string, List<int>> runs = Simulate(scheduler, 60.0, "tomb1", "tomb2", "tomb3");

            List<int> all = runs.Values.SelectMany(f => f).ToList();
            Assert.That(all.Count, Is.EqualTo(all.Distinct().Count()));
            Assert.That(runs.Values.All(f => f.Count >= 55), Is.True);
        }

        [Test]
        public void Phases_are_spread_over_the_interval()
        {
            var scheduler = new TickScheduler(seed: 3);
            for (int i = 0; i < 200; i++)
                scheduler.Register("t" + i, 10.0, () => { }, now: 0);

            int firstSecond = 0;
            for (double t = 0; t < 1.0; t += Frame)
                firstSecond += scheduler.Update(t);

            Assert.That(firstSecond, Is.LessThan(60), "ticks should not all fire at once");
        }

        [Test]
        public void Late_ticks_do_not_burst()
        {
            var scheduler = new TickScheduler(seed: 5);
            int count = 0;
            scheduler.Register("a", 1.0, () => count++, now: 0);

            scheduler.Update(100.0);
            scheduler.Update(100.0 + Frame);
            scheduler.Update(100.0 + 2 * Frame);

            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void Failing_tick_is_reported_and_others_keep_running()
        {
            var scheduler = new TickScheduler(seed: 2) { MaxTicksPerFrame = 10 };
            var failures = new List<string>();
            int ok = 0;
            scheduler.TickFailed += (h, _) => failures.Add(h.Name);
            scheduler.Register("bad", 1.0, () => throw new InvalidOperationException(), now: 0);
            scheduler.Register("good", 1.0, () => ok++, now: 0);

            for (double t = 0; t < 5.0; t += Frame)
                scheduler.Update(t);

            Assert.That(failures, Is.Not.Empty);
            Assert.That(failures.Distinct(), Is.EqualTo(new[] { "bad" }));
            Assert.That(ok, Is.GreaterThanOrEqualTo(4));
        }

        [Test]
        public void Unregistered_tick_stops()
        {
            var scheduler = new TickScheduler(seed: 4);
            int count = 0;
            TickHandle handle = scheduler.Register("a", 1.0, () => count++, now: 0);
            scheduler.Unregister(handle);

            for (double t = 0; t < 5.0; t += Frame)
                scheduler.Update(t);

            Assert.That(count, Is.Zero);
        }
    }
}
