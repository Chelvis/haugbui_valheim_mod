#if DEBUG
using Haugbui.Core.Creature;
using Haugbui.Integration;
using UnityEngine;

namespace Haugbui.SelfTest
{
    /// <summary>Clock for the self-test: day fraction set by the test, world time = offset + game time.</summary>
    internal sealed class FakeTime : ITimeSource
    {
        public const float Noon = 0.5f;
        public const float Night = 0.9f;

        public float Fraction = 0.5f;
        public double Offset = 100000;
        public bool IsNight => Fraction <= PresencePolicy.Dawn || Fraction >= PresencePolicy.Dusk;
        public float DayFraction => Fraction;
        public double DayLengthSeconds => 1800;
        public double Now => Offset + Time.timeAsDouble;
    }
}
#endif
