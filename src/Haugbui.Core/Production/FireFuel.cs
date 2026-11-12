using System;

namespace Haugbui.Core.Production
{
    /// <summary>Shown fuel is the ceiling of the stored value. A fire is refilled only at 0 or 1, then up to full.</summary>
    public static class FireFuel
    {
        public static bool NeedsRefill(float fuel) => fuel <= 1f;

        /// <summary>How many single units the game's add-fuel call will accept before the ceiling reaches the maximum.</summary>
        public static int UnitsToFill(float fuel, int maxFuel)
        {
            if (!NeedsRefill(fuel) || maxFuel <= 0) return 0;
            int adds = 0;
            double value = fuel;
            while (adds < maxFuel && Math.Ceiling(value) < maxFuel)
            {
                value = Math.Min(Math.Max(value, 0d), maxFuel) + 1d;
                if (value > maxFuel) value = maxFuel;
                adds++;
            }
            return adds;
        }
    }
}
