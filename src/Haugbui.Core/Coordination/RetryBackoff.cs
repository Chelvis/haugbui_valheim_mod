using System;
using System.Collections.Generic;

namespace Haugbui.Core.Coordination
{
    /// <summary>
    /// HB-065: per-key exponential backoff (e.g. an item kind without a destination). While a key waits, sources skip
    /// it; a success forgets it.
    /// </summary>
    public sealed class RetryBackoff
    {
        private readonly Dictionary<string, (double Next, double Delay)> _waiting = new Dictionary<string, (double, double)>();

        public RetryBackoff(double initialSeconds = 2, double maxSeconds = 60)
        {
            if (initialSeconds <= 0 || maxSeconds < initialSeconds) throw new ArgumentOutOfRangeException(nameof(initialSeconds));
            InitialSeconds = initialSeconds;
            MaxSeconds = maxSeconds;
        }

        public double InitialSeconds { get; }
        public double MaxSeconds { get; }
        public int Count => _waiting.Count;

        public bool IsWaiting(string key, double now) => _waiting.TryGetValue(key, out var entry) && now < entry.Next;

        /// <summary>The delay applied by the last failure, 0 when the key is not waiting.</summary>
        public double DelayOf(string key) => _waiting.TryGetValue(key, out var entry) ? entry.Delay : 0;

        public double NextTry(string key) => _waiting.TryGetValue(key, out var entry) ? entry.Next : double.NegativeInfinity;

        /// <summary>Records a failure; repeated failures before the next try do not shorten or stretch the wait.</summary>
        public void Fail(string key, double now)
        {
            if (_waiting.TryGetValue(key, out var entry))
            {
                if (now < entry.Next) return;
                double delay = Math.Min(MaxSeconds, entry.Delay * 2);
                _waiting[key] = (now + delay, delay);
            }
            else
            {
                _waiting[key] = (now + InitialSeconds, InitialSeconds);
            }
        }

        public void Succeed(string key) => _waiting.Remove(key);

        public void Clear() => _waiting.Clear();

        public IEnumerable<string> Keys => _waiting.Keys;
    }
}
