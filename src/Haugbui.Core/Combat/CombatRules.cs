using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Creature;

namespace Haugbui.Core.Combat
{
    public struct CombatInput
    {
        public HaugbuiState State;

        /// <summary>Longsword bound, Combat toggle on and acknowledged, game API available.</summary>
        public bool Enabled;

        /// <summary>Sleep fast mode (HB-046): no combat and no flight while the game skips time.</summary>
        public bool Paused;

        /// <summary>The dawn return lead, Rest or a lost Altar asked it to go home.</summary>
        public bool ReturnPending;

        public bool HasTarget;

        /// <summary>It was hurt recently or an attacker remains (<see cref="ThreatTracker"/>).</summary>
        public bool Threatened;
    }

    public enum CombatAction
    {
        None,
        Engage,
        Disengage,
        Flee,
        StopFleeing
    }

    /// <summary>
    /// HB-142/144: when to fight, flee or go back to work. No new fight once a return is pending; a fight in progress
    /// ends into the return. Combat off: only damage makes it flee, never being targeted.
    /// </summary>
    public static class CombatRules
    {
        public static CombatAction Decide(in CombatInput input)
        {
            switch (input.State)
            {
                case HaugbuiState.Combat:
                    if (input.Paused || !input.Enabled || input.ReturnPending || !input.HasTarget) return CombatAction.Disengage;
                    return CombatAction.None;
                case HaugbuiState.Fleeing:
                    if (input.Paused) return CombatAction.StopFleeing;
                    if (input.Enabled && input.HasTarget && !input.ReturnPending) return CombatAction.Engage;
                    return input.Threatened ? CombatAction.None : CombatAction.StopFleeing;
                case HaugbuiState.Idle:
                case HaugbuiState.Traveling:
                    if (input.Paused || input.ReturnPending) return CombatAction.None;
                    if (input.Enabled && input.HasTarget) return CombatAction.Engage;
                    // Hurt by something it may not fight (combat off, or the attacker is not a valid target).
                    return input.Threatened ? CombatAction.Flee : CombatAction.None;
                default:
                    return CombatAction.None;
            }
        }
    }

    public static class CombatMath
    {
        /// <summary>Damage factor of its hits: the level multiplier times the configured base multiplier.</summary>
        public static float DealtFactor(float levelMultiplier, float baseMultiplier) => Positive(levelMultiplier) * Positive(baseMultiplier);

        /// <summary>Factor applied to incoming damage (defence formula, to confirm: divided by the level multiplier).</summary>
        public static float TakenFactor(float levelMultiplier) => 1f / Positive(levelMultiplier);

        private static float Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 1f;
    }

    public enum AttackerStatus
    {
        /// <summary>Dead, removed, unloaded or out of the operational radius.</summary>
        Gone,
        Present,
        TargetingIt
    }

    /// <summary>
    /// HB-144: who hurt the Haugbui and whether any of them remains. An attacker remains while it is present and either
    /// targets the Haugbui or hit it within <see cref="MemorySeconds"/>; any damage keeps it threatened for
    /// <see cref="CalmSeconds"/>, also damage without an attacker.
    /// </summary>
    public sealed class ThreatTracker
    {
        public const double CalmSeconds = 5;
        public const double MemorySeconds = 10;

        private readonly Dictionary<string, double> _lastHitBy = new Dictionary<string, double>();
        private double _lastDamage = double.NegativeInfinity;

        public int Attackers => _lastHitBy.Count;
        public double LastDamage => _lastDamage;

        public void Damaged(double now, string attacker)
        {
            _lastDamage = now;
            if (!string.IsNullOrEmpty(attacker)) _lastHitBy[attacker] = now;
        }

        public bool HitRecently(string attacker, double now) =>
            !string.IsNullOrEmpty(attacker) && _lastHitBy.TryGetValue(attacker, out double at) && now - at < MemorySeconds;

        /// <summary>Forgets attackers that no longer count; true while it should keep away.</summary>
        public bool Threatened(double now, System.Func<string, AttackerStatus> status)
        {
            foreach (string attacker in _lastHitBy.Keys.ToList())
            {
                AttackerStatus s = status(attacker);
                bool remains = s == AttackerStatus.TargetingIt || s == AttackerStatus.Present && now - _lastHitBy[attacker] < MemorySeconds;
                if (!remains) _lastHitBy.Remove(attacker);
            }
            return now - _lastDamage < CalmSeconds || _lastHitBy.Count > 0;
        }

        public void Clear()
        {
            _lastHitBy.Clear();
            _lastDamage = double.NegativeInfinity;
        }
    }
}
