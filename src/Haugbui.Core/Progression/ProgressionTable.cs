using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Haugbui.Core.Progression
{
    public readonly struct HaugbuiAttributes
    {
        public HaugbuiAttributes(int level, float carryCapacity, float maxSpeed, float radius, int slots, float maxHealth,
            float combatMultiplier)
        {
            Level = level;
            CarryCapacity = carryCapacity;
            MaxSpeed = maxSpeed;
            Radius = radius;
            Slots = slots;
            MaxHealth = maxHealth;
            CombatMultiplier = combatMultiplier;
        }

        public int Level { get; }
        public float CarryCapacity { get; }
        public float MaxSpeed { get; }
        public float Radius { get; }
        public int Slots { get; }
        public float MaxHealth { get; }

        /// <summary>HB-142: multiplies the damage of its hits and divides the damage it takes.</summary>
        public float CombatMultiplier { get; }
    }

    public sealed class ProgressionTable
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 6;
        public const int LevelCount = MaxLevel - MinLevel + 1;

        public static readonly float[] DefaultCarryCapacity = { 50, 70, 90, 110, 130, 150 };
        public static readonly float[] DefaultMaxSpeed = { 2.0f, 2.8f, 3.6f, 4.4f, 5.2f, 6.0f };
        public static readonly float[] DefaultRadius = { 50, 60, 70, 80, 90, 100 };
        public static readonly int[] DefaultSlots = { 2, 3, 4, 5, 6, 6 };
        public static readonly float[] DefaultMaxHealth = { 20, 40, 60, 80, 100, 120 };
        public static readonly float[] DefaultCombatMultiplier = { 1.0f, 1.2f, 1.4f, 1.6f, 1.8f, 2.0f };

        private readonly float[] _carryCapacity;
        private readonly float[] _maxSpeed;
        private readonly float[] _radius;
        private readonly int[] _slots;
        private readonly float[] _maxHealth;
        private readonly float[] _combatMultiplier;

        private ProgressionTable(float[] carryCapacity, float[] maxSpeed, float[] radius, int[] slots, float[] maxHealth,
            float[] combatMultiplier)
        {
            _carryCapacity = carryCapacity;
            _maxSpeed = maxSpeed;
            _radius = radius;
            _slots = slots;
            _maxHealth = maxHealth;
            _combatMultiplier = combatMultiplier;
        }

        public static ProgressionTable Default { get; } = new ProgressionTable(
            DefaultCarryCapacity, DefaultMaxSpeed, DefaultRadius, DefaultSlots, DefaultMaxHealth, DefaultCombatMultiplier);

        public HaugbuiAttributes ForLevel(int level)
        {
            int clamped = Math.Max(MinLevel, Math.Min(MaxLevel, level));
            int i = clamped - MinLevel;
            return new HaugbuiAttributes(clamped, _carryCapacity[i], _maxSpeed[i], _radius[i], _slots[i], _maxHealth[i],
                _combatMultiplier[i]);
        }

        /// <summary>
        /// Builds a table from per-level CSV values. Each invalid column falls back to its default and adds a warning,
        /// so one bad config line never resets the others.
        /// </summary>
        public static ProgressionTable FromCsv(
            string carryCapacity, string maxSpeed, string radius, string slots, string maxHealth, string combatMultiplier,
            out IReadOnlyList<string> warnings)
        {
            var problems = new List<string>();
            var table = new ProgressionTable(
                ParseColumn("CarryCapacity", carryCapacity, DefaultCarryCapacity, problems),
                ParseColumn("MaxSpeed", maxSpeed, DefaultMaxSpeed, problems),
                ParseColumn("Radius", radius, DefaultRadius, problems),
                ParseColumn("Slots", slots, DefaultSlots.Select(s => (float)s).ToArray(), problems, integer: true)
                    .Select(v => (int)v).ToArray(),
                ParseColumn("MaxHealth", maxHealth, DefaultMaxHealth, problems),
                ParseColumn("CombatMultiplier", combatMultiplier, DefaultCombatMultiplier, problems));
            warnings = problems;
            return table;
        }

        public static string ToCsv(IEnumerable<float> values) =>
            string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        public static string ToCsv(IEnumerable<int> values) =>
            string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

        private static float[] ParseColumn(string name, string csv, float[] fallback, List<string> problems, bool integer = false)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                problems.Add($"{name}: empty, using defaults");
                return fallback;
            }

            string[] parts = csv.Split(',');
            if (parts.Length != LevelCount)
            {
                problems.Add($"{name}: expected {LevelCount} values, got {parts.Length}; using defaults");
                return fallback;
            }

            var result = new float[LevelCount];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    || float.IsNaN(value) || float.IsInfinity(value) || value <= 0
                    || (integer && Math.Abs(value - Math.Round(value)) > 0.0001f))
                {
                    problems.Add($"{name}: invalid value '{parts[i].Trim()}' at level {i + MinLevel}; using defaults");
                    return fallback;
                }
                result[i] = value;
            }
            return result;
        }
    }
}
