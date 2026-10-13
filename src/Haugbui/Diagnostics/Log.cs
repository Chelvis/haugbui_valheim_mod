using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace Haugbui.Diagnostics
{
    internal static class Log
    {
        private const double RateLimitSeconds = 10;

        private static ManualLogSource _source;
        private static readonly Dictionary<string, DateTime> LastByKey = new Dictionary<string, DateTime>();

        public static bool DebugEnabled { get; set; }

        public static void Init(ManualLogSource source) => _source = source;

        public static void Info(string message) => _source?.LogInfo(message);
        public static void Warning(string message) => _source?.LogWarning(message);
        public static void Error(string message) => _source?.LogError(message);

        public static void Debug(string message)
        {
            if (DebugEnabled) _source?.LogInfo("[debug] " + message);
        }

        /// <summary>Logs at most once per <see cref="RateLimitSeconds"/> per key; for errors raised from per-frame code.</summary>
        public static void ErrorRateLimited(string key, string message)
        {
            DateTime now = DateTime.UtcNow;
            if (LastByKey.TryGetValue(key, out DateTime last) && (now - last).TotalSeconds < RateLimitSeconds) return;
            LastByKey[key] = now;
            Error(message);
        }
    }
}
