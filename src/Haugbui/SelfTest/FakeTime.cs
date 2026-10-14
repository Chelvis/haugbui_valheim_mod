#if DEBUG
using Haugbui.Core.Creature;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>
    /// Clock for the self-test: world time = offset + game time, so time keeps passing; setting a day fraction moves the
    /// clock forward to the next moment with that fraction, never back. A long day keeps a night longer than any suite.
    /// </summary>
    internal sealed class FakeTime : ITimeSource
    {
        public const float Noon = 0.5f;
        public const float Night = 0.9f;
        public const double DayLength = 7200;

        public double Offset = 100 * DayLength;

        public double DayLengthSeconds => DayLength;
        public double Now => Offset + Time.timeAsDouble;
        public bool IsNight => WorldClock.IsNight(Now, DayLength);
        public float DayFraction => WorldClock.Fraction(Now, DayLength);

        public float Fraction
        {
            get => DayFraction;
            set
            {
                double target = WorldClock.Unscale(value);
                double current = WorldClock.RawFraction(Now, DayLength);
                double delta = target - current;
                if (delta < -1e-9) delta += 1;
                Offset += delta * DayLength;
            }
        }

        /// <summary>Jumps to the next dusk plus a little, like waiting for the next night.</summary>
        public void ToNextDusk(double after = 5)
        {
            Offset += WorldClock.NextDusk(Now, DayLength) - Now + after;
        }
    }
}
#endif
