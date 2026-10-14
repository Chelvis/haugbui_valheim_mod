using System;

namespace Haugbui.Core.Creature
{
    public struct PresenceInput
    {
        public double Now;
        public double DayLengthSeconds;
        public bool FullTime;
        public bool Resting;
        public bool Dead;
        public double DeathTime;
        public double CooldownSeconds;
        public float DawnReturnLeadSeconds;

        /// <summary>HB-136: a new tomb works from its first dusk on; NaN for tombs built before that rule.</summary>
        public double StartTime;
    }

    public enum PresenceDecision
    {
        StayInside,
        Emerge,
        StayOut,
        Return
    }

    /// <summary>HB-040..043, HB-132..136: when the Haugbui may be out of its tomb (tribute aside).</summary>
    public static class PresencePolicy
    {
        /// <summary>EnvMan.IsDay/IsNight boundaries on the visible day fraction.</summary>
        public const float Dawn = 0.25f;
        public const float Dusk = 0.75f;

        public static PresenceDecision Decide(PresenceInput input, bool creatureOut)
        {
            bool shouldBeOut = ShouldBeOut(input);
            if (creatureOut) return shouldBeOut ? PresenceDecision.StayOut : PresenceDecision.Return;
            return shouldBeOut ? PresenceDecision.Emerge : PresenceDecision.StayInside;
        }

        public static bool ShouldBeOut(PresenceInput input)
        {
            if (input.Resting) return false;
            if (WaitRemaining(input) > 0) return false;
            return InWorkWindow(input);
        }

        /// <summary>Night before the dawn lead, or any time with the Altar.</summary>
        public static bool InWorkWindow(PresenceInput input) =>
            input.FullTime
            || WorldClock.IsNight(input.Now, input.DayLengthSeconds) && SecondsToDawn(input) > input.DawnReturnLeadSeconds;

        public static double SecondsToDawn(PresenceInput input)
        {
            long shift = WorldClock.ShiftId(input.Now, input.DayLengthSeconds);
            if (!WorldClock.IsNightShift(shift)) return 0;
            return WorldClock.ShiftEnd(shift, input.DayLengthSeconds) - input.Now;
        }

        /// <summary>
        /// HB-133: world time when a dead Haugbui may come back: the cooldown end with the Altar, otherwise the first dusk
        /// at or after it. NaN while alive. A death time in the future (world time rolled back) counts as now.
        /// </summary>
        public static double ReturnTime(PresenceInput input)
        {
            if (!input.Dead || double.IsNaN(input.DeathTime)) return double.NaN;
            double cooldownEnd = Math.Min(input.DeathTime, input.Now) + input.CooldownSeconds;
            return input.FullTime ? cooldownEnd : WorldClock.DuskAtOrAfter(cooldownEnd, input.DayLengthSeconds);
        }

        /// <summary>HB-132/136: seconds until death or a new tomb no longer keep it inside; the later of the two applies.</summary>
        public static double WaitRemaining(PresenceInput input)
        {
            double until = double.IsNaN(input.StartTime) ? double.NegativeInfinity : input.StartTime;
            double back = ReturnTime(input);
            if (!double.IsNaN(back)) until = Math.Max(until, back);
            return Math.Max(0, until - input.Now);
        }

        /// <summary>World-time cooldown left; 0 when never died.</summary>
        public static double CooldownRemaining(double now, double deathTime, double cooldownSeconds)
        {
            if (double.IsNaN(deathTime)) return 0;
            double elapsed = Math.Max(0, now - deathTime);
            return Math.Max(0, cooldownSeconds - elapsed);
        }

        /// <summary>
        /// HB-169: while inside its work window, the time left in it; otherwise the time until the next one opens.
        /// </summary>
        public static ShiftTimer Timer(PresenceInput input)
        {
            double wait = WaitRemaining(input);
            if (input.Resting || wait > 0)
                return new ShiftTimer(false, input.Resting ? double.NaN : Math.Max(wait, UntilWindow(input, input.Now + wait)));
            if (InWorkWindow(input))
            {
                long shift = WorldClock.ShiftId(input.Now, input.DayLengthSeconds);
                double end = WorldClock.ShiftEnd(shift, input.DayLengthSeconds);
                if (!input.FullTime) end -= input.DawnReturnLeadSeconds;
                return new ShiftTimer(true, Math.Max(0, end - input.Now));
            }
            return new ShiftTimer(false, UntilWindow(input, input.Now));
        }

        private static double UntilWindow(PresenceInput input, double from)
        {
            if (input.FullTime) return from - input.Now;
            var probe = input;
            probe.Now = from;
            if (InWorkWindow(probe)) return from - input.Now;
            return WorldClock.NextDusk(from, input.DayLengthSeconds) - input.Now;
        }

        public static string FormatCountdown(double seconds)
        {
            var total = (long)Math.Ceiling(Math.Max(0, seconds));
            long h = total / 3600, m = total % 3600 / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
    }

    public readonly struct ShiftTimer
    {
        public ShiftTimer(bool inShift, double seconds)
        {
            InShift = inShift;
            Seconds = seconds;
        }

        /// <summary>True: <see cref="Seconds"/> left in the current shift; false: until the next one (NaN while resting).</summary>
        public bool InShift { get; }
        public double Seconds { get; }
    }

    public static class HealthMath
    {
        /// <summary>Linear regeneration from 0 to <paramref name="max"/> in <paramref name="fullHealSeconds"/>.</summary>
        public static float Regenerate(float current, float max, double elapsedSeconds, double fullHealSeconds)
        {
            if (elapsedSeconds <= 0 || fullHealSeconds <= 0) return Math.Min(current, max);
            double healed = current + max * (elapsedSeconds / fullHealSeconds);
            return (float)Math.Min(max, healed);
        }

        /// <summary>Keeps the health fraction when max health changes with the level.</summary>
        public static float Rescale(float current, float oldMax, float newMax)
        {
            if (oldMax <= 0) return newMax;
            return Math.Max(0, Math.Min(newMax, current / oldMax * newMax));
        }
    }

    public static class Steering
    {
        /// <summary>
        /// Next speed toward a target: accelerates to <paramref name="maxSpeed"/> and brakes so it can stop at the
        /// target (v² = 2·a·d), never exceeding the max.
        /// </summary>
        public static float NextSpeed(float currentSpeed, float distance, float maxSpeed, float acceleration, float dt)
        {
            float brake = (float)Math.Sqrt(Math.Max(0, 2 * acceleration * distance));
            float desired = Math.Min(maxSpeed, brake);
            float step = acceleration * dt;
            float next = currentSpeed < desired ? Math.Min(desired, currentSpeed + step) : Math.Max(desired, currentSpeed - step);
            return Math.Max(0, Math.Min(maxSpeed, next));
        }

        /// <summary>Distance moved this frame, never past the target and never more than <paramref name="maxStep"/>.</summary>
        public static float StepLength(float speed, float distance, float dt, float maxStep) =>
            Math.Min(Math.Min(speed * dt, distance), maxStep);
    }
}
