using System.Collections.Generic;

namespace Haugbui.Core.Coordination
{
    /// <summary>
    /// The Haugbui fills its bag before it unloads. Deposit and station feeding wait while it is under
    /// <see cref="UnloadFraction"/> of its weight and something still fits to pick up. Delivery and a done item
    /// that would burn are never postponed.
    /// </summary>
    public static class CarryCycle
    {
        public const float UnloadFraction = 0.8f;

        /// <summary>Below every pickup, so a held unload loses to harvest, collection and ground items.</summary>
        public const int HeldPriority = 25;

        public static bool ShouldUnload(float weight, float capacity) =>
            !(capacity > 0f) || weight >= capacity * UnloadFraction;

        public static bool IsPickup(string kind) =>
            kind == "harvest" || kind == "gather" || kind == "collect" || kind == "sort_tomb" || kind == "production_outputs";

        public static bool IsUnload(string kind) => kind == "deposit" || kind == "production_feed";

        public static void HoldUnload(IList<TaskCandidate> candidates, float weight, float capacity)
        {
            if (candidates == null || ShouldUnload(weight, capacity)) return;
            bool pickup = false;
            for (int i = 0; i < candidates.Count; i++)
                if (IsPickup(candidates[i].Kind))
                {
                    pickup = true;
                    break;
                }
            if (!pickup) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                TaskCandidate candidate = candidates[i];
                if (candidate.Urgent || !IsUnload(candidate.Kind)) continue;
                candidate.Priority = HeldPriority;
            }
        }
    }
}
