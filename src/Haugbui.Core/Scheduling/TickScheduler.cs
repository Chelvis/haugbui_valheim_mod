using System;
using System.Collections.Generic;

namespace Haugbui.Core.Scheduling
{
    public sealed class TickHandle
    {
        internal TickHandle(string name, double interval, Action tick)
        {
            Name = name;
            Interval = interval;
            Tick = tick;
        }

        public string Name { get; }
        public double Interval { get; internal set; }
        internal Action Tick { get; }
        internal double NextDue { get; set; }
        public long RunCount { get; internal set; }
        public double LastDurationMs { get; internal set; }
    }

    /// <summary>
    /// Runs registered ticks at their interval with spread phases and at most <see cref="MaxTicksPerFrame"/> ticks per
    /// frame, so several tombs never plan in the same frame.
    /// </summary>
    public sealed class TickScheduler
    {
        private readonly List<TickHandle> _handles = new List<TickHandle>();
        private readonly Random _random;
        private readonly Func<double> _stopwatchMs;

        public TickScheduler(int seed = 0, Func<double> stopwatchMs = null)
        {
            _random = seed == 0 ? new Random() : new Random(seed);
            _stopwatchMs = stopwatchMs;
        }

        public int MaxTicksPerFrame { get; set; } = 1;

        public IReadOnlyList<TickHandle> Handles => _handles;

        public event Action<TickHandle, Exception> TickFailed;

        public TickHandle Register(string name, double intervalSeconds, Action tick, double now)
        {
            if (intervalSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            if (tick == null) throw new ArgumentNullException(nameof(tick));

            var handle = new TickHandle(name, intervalSeconds, tick)
            {
                NextDue = now + _random.NextDouble() * intervalSeconds
            };
            _handles.Add(handle);
            return handle;
        }

        public void Unregister(TickHandle handle)
        {
            if (handle == null) return;
            _handles.Remove(handle);
        }

        /// <returns>Number of ticks executed this frame.</returns>
        public int Update(double now)
        {
            int executed = 0;
            while (executed < MaxTicksPerFrame)
            {
                TickHandle next = null;
                foreach (TickHandle handle in _handles)
                {
                    if (handle.NextDue <= now && (next == null || handle.NextDue < next.NextDue))
                        next = handle;
                }
                if (next == null) break;

                // Late ticks are rescheduled from now, not from their missed due time, to avoid catch-up bursts.
                next.NextDue = Math.Max(next.NextDue + next.Interval, now + next.Interval * 0.5);
                Run(next);
                executed++;
            }
            return executed;
        }

        private void Run(TickHandle handle)
        {
            double start = _stopwatchMs?.Invoke() ?? 0;
            try
            {
                handle.Tick();
            }
            catch (Exception e)
            {
                TickFailed?.Invoke(handle, e);
            }
            finally
            {
                handle.RunCount++;
                if (_stopwatchMs != null) handle.LastDurationMs = _stopwatchMs() - start;
            }
        }
    }
}
