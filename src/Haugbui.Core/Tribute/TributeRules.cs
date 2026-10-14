using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Haugbui.Core.Progression;

namespace Haugbui.Core.Tribute
{
    /// <summary>HB-165/166: what a shift costs per level and what each valuable is worth, in coin units.</summary>
    public sealed class TributeTable
    {
        public static readonly int[] DefaultCosts = { 5, 5, 10, 10, 15, 15 };

        public static readonly IReadOnlyList<KeyValuePair<string, int>> DefaultValues = new[]
        {
            new KeyValuePair<string, int>("Coins", 1),
            new KeyValuePair<string, int>("Amber", 5),
            new KeyValuePair<string, int>("AmberPearl", 10),
            new KeyValuePair<string, int>("Ruby", 20),
            new KeyValuePair<string, int>("SilverNecklace", 30),
        };

        public TributeTable(IReadOnlyList<int> costs, IReadOnlyList<KeyValuePair<string, int>> values)
        {
            Costs = costs;
            Values = values;
        }

        public IReadOnlyList<int> Costs { get; }

        /// <summary>Offer order of the panel buttons.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Values { get; }

        public int CostFor(int level)
        {
            int index = Math.Max(ProgressionTable.MinLevel, Math.Min(ProgressionTable.MaxLevel, level)) - ProgressionTable.MinLevel;
            return Costs[index];
        }

        public int ValueOf(string prefab)
        {
            foreach (KeyValuePair<string, int> value in Values)
                if (value.Key == prefab) return value.Value;
            return 0;
        }

        public static TributeTable Default => new TributeTable(DefaultCosts, DefaultValues);

        public static string CostsToCsv(IEnumerable<int> costs) => string.Join(",", costs.Select(c => c.ToString(CultureInfo.InvariantCulture)));

        public static string ValuesToConfig(IEnumerable<KeyValuePair<string, int>> values) =>
            string.Join(",", values.Select(v => $"{v.Key}:{v.Value.ToString(CultureInfo.InvariantCulture)}"));

        /// <summary>Invalid text falls back to the defaults with a warning, never to zero costs.</summary>
        public static TributeTable FromConfig(string costsCsv, string valuesText, out List<string> warnings)
        {
            warnings = new List<string>();
            int[] costs = ParseCosts(costsCsv);
            if (costs == null)
            {
                warnings.Add($"Tribute cost per level '{costsCsv}' is invalid (six whole numbers ≥ 0); using {CostsToCsv(DefaultCosts)}.");
                costs = DefaultCosts;
            }
            List<KeyValuePair<string, int>> values = ParseValues(valuesText);
            if (values == null)
            {
                warnings.Add($"Tribute values '{valuesText}' are invalid (Item:Value,...); using {ValuesToConfig(DefaultValues)}.");
                values = DefaultValues.ToList();
            }
            return new TributeTable(costs, values);
        }

        private static int[] ParseCosts(string csv)
        {
            string[] parts = (csv ?? string.Empty).Split(',');
            if (parts.Length != ProgressionTable.MaxLevel - ProgressionTable.MinLevel + 1) return null;
            var costs = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out costs[i]) || costs[i] < 0) return null;
            return costs;
        }

        private static List<KeyValuePair<string, int>> ParseValues(string text)
        {
            var values = new List<KeyValuePair<string, int>>();
            foreach (string part in (text ?? string.Empty).Split(','))
            {
                string[] pair = part.Split(':');
                if (pair.Length != 2) return null;
                string prefab = pair[0].Trim();
                if (prefab.Length == 0 || values.Any(v => v.Key == prefab)) return null;
                if (!int.TryParse(pair[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0) return null;
                values.Add(new KeyValuePair<string, int>(prefab, value));
            }
            return values.Count > 0 ? values : null;
        }
    }

    /// <summary>
    /// HB-166/HB-159: the tomb's credit in coin units. <see cref="Cheated"/> is the part paid with cheated valuables; it is
    /// spent first, so a refund never returns more clean coins than were offered clean, nor turns cheated value clean.
    /// </summary>
    public sealed class TributeBalance
    {
        public int Total { get; private set; }
        public int Cheated { get; private set; }
        public int Clean => Total - Cheated;

        public TributeBalance(int total = 0, int cheated = 0)
        {
            Total = Math.Max(0, total);
            Cheated = Math.Max(0, Math.Min(Total, cheated));
        }

        public void Offer(int value, bool cheated)
        {
            if (value <= 0) return;
            Total += value;
            if (cheated) Cheated += value;
        }

        public bool TryCharge(int cost)
        {
            if (cost < 0 || Total < cost) return false;
            Total -= cost;
            Cheated = Math.Max(0, Cheated - cost);
            return true;
        }

        /// <summary>Empties the balance; returns (clean, cheated) coins to give back.</summary>
        public (int Clean, int Cheated) TakeAll()
        {
            (int, int) refund = (Clean, Cheated);
            Total = 0;
            Cheated = 0;
            return refund;
        }
    }

    public enum ShiftDecision
    {
        /// <summary>TributeEnabled = false: the shift is free.</summary>
        Free,

        /// <summary>This shift was already paid (reload, Rest toggled, Altar).</summary>
        AlreadyPaid,

        /// <summary>Nothing to do: stays dormant, charges nothing, checks again later.</summary>
        NoWork,

        /// <summary>Work and enough credit: charge now.</summary>
        Charge,

        /// <summary>Work but not enough credit: "no tribute".</summary>
        NoTribute
    }

    /// <summary>HB-167/168: a shift is charged once, at its start, and only when there is work.</summary>
    public static class ShiftRules
    {
        public static ShiftDecision Decide(bool tributeEnabled, long shift, long lastPaidShift, bool hasWork, int balance, int cost)
        {
            if (!tributeEnabled) return ShiftDecision.Free;
            if (shift == lastPaidShift) return ShiftDecision.AlreadyPaid;
            if (!hasWork) return ShiftDecision.NoWork;
            return balance >= cost ? ShiftDecision.Charge : ShiftDecision.NoTribute;
        }

        public static bool MayWork(ShiftDecision decision) =>
            decision == ShiftDecision.Free || decision == ShiftDecision.AlreadyPaid || decision == ShiftDecision.Charge;
    }
}
