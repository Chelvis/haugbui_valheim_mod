using System;

namespace Haugbui.Core.Creature
{
    /// <summary>
    /// Day, night and shifts from world time alone, so absolute times (next dusk, shift ends) can be stored.
    /// EnvMan.RescaleDayFraction (1.0.17) maps the raw part of the day 0.15..0.85 to the visible fraction 0.25..0.75, and
    /// EnvMan.IsNight is "visible fraction ≤ 0.25 or ≥ 0.75": night is raw ≤ 0.15 or ≥ 0.85, 30% of the day.
    /// </summary>
    public static class WorldClock
    {
        public const double MorningRaw = 0.15;
        public const double EveningRaw = 0.85;

        /// <summary>Shift id meaning "never paid".</summary>
        public const long NoShift = long.MinValue;

        public static long Day(double now, double dayLength) => (long)Math.Floor(now / dayLength);

        public static double RawFraction(double now, double dayLength)
        {
            double f = now / dayLength;
            return f - Math.Floor(f);
        }

        /// <summary>The fraction EnvMan shows (sun position), before its smoothing.</summary>
        public static float Fraction(double now, double dayLength) => (float)Rescale(RawFraction(now, dayLength));

        public static double Rescale(double raw)
        {
            if (raw >= MorningRaw && raw <= EveningRaw) return 0.25 + (raw - MorningRaw) / (EveningRaw - MorningRaw) * 0.5;
            if (raw < 0.5) return raw / MorningRaw * 0.25;
            return 0.75 + (raw - EveningRaw) / (1 - EveningRaw) * 0.25;
        }

        public static double Unscale(double fraction)
        {
            fraction -= Math.Floor(fraction);
            if (fraction >= 0.25 && fraction <= 0.75) return MorningRaw + (fraction - 0.25) / 0.5 * (EveningRaw - MorningRaw);
            if (fraction < 0.5) return fraction / 0.25 * MorningRaw;
            return EveningRaw + (fraction - 0.75) / 0.25 * (1 - EveningRaw);
        }

        public static bool IsNight(double now, double dayLength)
        {
            double raw = RawFraction(now, dayLength);
            return raw <= MorningRaw || raw >= EveningRaw;
        }

        /// <summary>
        /// HB-164: day d has the day shift 2d (morning to evening) and the night shift 2d+1 (evening to the next morning),
        /// so the hours after midnight still belong to the night that began the evening before.
        /// </summary>
        public static long ShiftId(double now, double dayLength)
        {
            long day = Day(now, dayLength);
            double raw = RawFraction(now, dayLength);
            if (raw < MorningRaw) return 2 * (day - 1) + 1;
            return raw < EveningRaw ? 2 * day : 2 * day + 1;
        }

        public static bool IsNightShift(long shift) => (shift & 1) == 1;

        public static double ShiftStart(long shift, double dayLength)
        {
            long day = FloorDiv2(shift);
            return IsNightShift(shift) ? (day + EveningRaw) * dayLength : (day + MorningRaw) * dayLength;
        }

        public static double ShiftEnd(long shift, double dayLength)
        {
            long day = FloorDiv2(shift);
            return IsNightShift(shift) ? (day + 1 + MorningRaw) * dayLength : (day + EveningRaw) * dayLength;
        }

        /// <summary>The first dusk (start of a night) at or after <paramref name="time"/>.</summary>
        public static double DuskAtOrAfter(double time, double dayLength)
        {
            double dusk = (Day(time, dayLength) + EveningRaw) * dayLength;
            return dusk >= time ? dusk : dusk + dayLength;
        }

        /// <summary>HB-136: the start of the next night; during a night that is tomorrow's dusk.</summary>
        public static double NextDusk(double now, double dayLength)
        {
            double dusk = (Day(now, dayLength) + EveningRaw) * dayLength;
            return dusk > now ? dusk : dusk + dayLength;
        }

        private static long FloorDiv2(long value) => value >= 0 ? value / 2 : (value - 1) / 2;
    }
}
