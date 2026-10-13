using System;

namespace Haugbui.Core.Creature
{
    public struct PresenceInput
    {
        public bool IsNight;
        public float DayFraction;
        public double DayLengthSeconds;
        public bool FullTime;
        public bool Resting;
        public bool Dead;
        public double Now;
        public double DeathTime;
        public double CooldownSeconds;
        public float DawnReturnLeadSeconds;
    }

    public enum PresenceDecision
    {
        StayInside,
        Emerge,
        StayOut,
        Return
    }

    /// <summary>HB-040..043, HB-132..135: when the Haugbui should be out of its tomb.</summary>
    public static class PresencePolicy
    {
        /// <summary>EnvMan.IsDay/IsNight boundaries: night is day fraction ≤ 0.25 or ≥ 0.75.</summary>
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
            if (input.Dead && CooldownRemaining(input.Now, input.DeathTime, input.CooldownSeconds) > 0) return false;
            if (input.FullTime) return true;
            return input.IsNight && SecondsToDawn(input.DayFraction, input.DayLengthSeconds) > input.DawnReturnLeadSeconds;
        }

        public static double SecondsToDawn(float dayFraction, double dayLengthSeconds)
        {
            double f = dayFraction - Math.Floor(dayFraction);
            double untilDawn = Dawn - f;
            if (untilDawn < 0) untilDawn += 1;
            return untilDawn * dayLengthSeconds;
        }

        /// <summary>
        /// World-time cooldown left; 0 when never died. A death time in the future (world time rolled back) counts as
        /// having just died, so the wait never exceeds one full cooldown.
        /// </summary>
        public static double CooldownRemaining(double now, double deathTime, double cooldownSeconds)
        {
            if (double.IsNaN(deathTime)) return 0;
            double elapsed = Math.Max(0, now - deathTime);
            return Math.Max(0, cooldownSeconds - elapsed);
        }

        public static string FormatCountdown(double seconds)
        {
            var total = (long)Math.Ceiling(Math.Max(0, seconds));
            long h = total / 3600, m = total % 3600 / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
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
