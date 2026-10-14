using System;
using System.Collections.Generic;
using System.Linq;

namespace Haugbui.Core.Coordination
{
    public readonly struct Lease
    {
        public Lease(string key, string holder, double expiresAt)
        {
            Key = key;
            Holder = holder;
            ExpiresAt = expiresAt;
        }

        public string Key { get; }
        public string Holder { get; }
        public double ExpiresAt { get; }
    }

    /// <summary>
    /// HB-156/157, ARCHITECTURE §6: transient leases so two Haugbuis never plan for the same target. Never persisted;
    /// cleared on world load; an unrenewed lease expires on its own.
    /// </summary>
    public sealed class ReservationTable
    {
        private readonly Dictionary<string, Lease> _leases = new Dictionary<string, Lease>();
        private readonly Func<double> _clock;

        public ReservationTable(Func<double> clock, double leaseSeconds = 30)
        {
            _clock = clock;
            LeaseSeconds = leaseSeconds;
        }

        public double LeaseSeconds { get; set; }

        public int Count
        {
            get
            {
                Expire();
                return _leases.Count;
            }
        }

        public IReadOnlyList<Lease> Leases
        {
            get
            {
                Expire();
                return _leases.Values.OrderBy(l => l.Key, StringComparer.Ordinal).ToList();
            }
        }

        public bool IsAvailable(string key, string holder)
        {
            if (!_leases.TryGetValue(key, out Lease lease)) return true;
            return lease.Holder == holder || lease.ExpiresAt <= _clock();
        }

        public string HolderOf(string key) =>
            _leases.TryGetValue(key, out Lease lease) && lease.ExpiresAt > _clock() ? lease.Holder : null;

        /// <summary>All or nothing: reserves every key only when each is free or already held by this holder.</summary>
        public bool TryReserveAll(IEnumerable<string> keys, string holder)
        {
            List<string> list = keys?.Distinct().ToList() ?? new List<string>();
            if (list.Any(k => !IsAvailable(k, holder))) return false;
            double expires = _clock() + LeaseSeconds;
            foreach (string key in list) _leases[key] = new Lease(key, holder, expires);
            return true;
        }

        public bool TryReserve(string key, string holder) => TryReserveAll(new[] { key }, holder);

        public void Renew(string holder)
        {
            double expires = _clock() + LeaseSeconds;
            foreach (Lease lease in _leases.Values.Where(l => l.Holder == holder).ToList())
                _leases[lease.Key] = new Lease(lease.Key, holder, expires);
        }

        public void Release(string key, string holder)
        {
            if (_leases.TryGetValue(key, out Lease lease) && lease.Holder == holder) _leases.Remove(key);
        }

        public int ReleaseAll(string holder)
        {
            List<string> keys = _leases.Values.Where(l => l.Holder == holder).Select(l => l.Key).ToList();
            foreach (string key in keys) _leases.Remove(key);
            return keys.Count;
        }

        public void Clear() => _leases.Clear();

        private void Expire()
        {
            double now = _clock();
            foreach (string key in _leases.Values.Where(l => l.ExpiresAt <= now).Select(l => l.Key).ToList())
                _leases.Remove(key);
        }
    }
}
