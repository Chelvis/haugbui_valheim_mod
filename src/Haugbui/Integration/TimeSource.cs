namespace Haugbui.Integration
{
    internal interface ITimeSource
    {
        bool IsNight { get; }
        float DayFraction { get; }
        double DayLengthSeconds { get; }

        /// <summary>World time (ZNet), saved with the world and paused while the game is closed.</summary>
        double Now { get; }
    }

    /// <summary>HB-040: EnvMan.IsNight is weather-independent; IsDaylight is not and must never be used here.</summary>
    internal sealed class GameTimeSource : ITimeSource
    {
        public bool IsNight => EnvMan.IsNight();
        public float DayFraction => EnvMan.instance != null ? EnvMan.instance.GetDayFraction() : 0.5f;
        public double DayLengthSeconds => EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1800;
        public double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
    }

    internal static class TimeSource
    {
        private static readonly ITimeSource Game = new GameTimeSource();

        /// <summary>Self-test hook; null uses the game clock.</summary>
        public static ITimeSource Override { get; set; }

        public static ITimeSource Current => Override ?? Game;
    }
}
