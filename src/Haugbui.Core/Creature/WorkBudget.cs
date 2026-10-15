using System;

namespace Haugbui.Core.Creature
{
    /// <summary>
    /// HB-046 / HB-T-098: while the game skips time (sleeping), the world time the Haugbui may work is banked here and
    /// spent on steps that cost travel plus interaction time, so a sleeping player loses none of the servant's night and
    /// it never does more than it could have done awake. Shared with away mode (M12).
    /// </summary>
    public sealed class WorkBudget
    {
        /// <summary>Interaction time charged per step, on top of travel.</summary>
        public const double InteractionSeconds = 1.5;

        private double _lastNow = double.NaN;

        /// <summary>Upper bound on banked time; a whole night is well below it.</summary>
        public double MaxSeconds { get; set; } = 6 * 3600;

        public double Seconds { get; private set; }

        /// <summary>
        /// Called every frame. World time passing while <paramref name="skipping"/> and <paramref name="mayWork"/> is
        /// banked; outside a skip the bank is emptied (normal work takes over).
        /// </summary>
        public void Observe(double now, bool skipping, bool mayWork)
        {
            if (skipping && mayWork && !double.IsNaN(_lastNow) && now > _lastNow)
                Seconds = Math.Min(MaxSeconds, Seconds + (now - _lastNow));
            if (!skipping) Seconds = 0;
            _lastNow = now;
        }

        public static double StepCost(double distance, double speed) => Math.Max(0, distance) / Math.Max(0.1, speed) + InteractionSeconds;

        public bool TrySpend(double seconds)
        {
            if (seconds > Seconds) return false;
            Seconds -= seconds;
            return true;
        }
    }
}
