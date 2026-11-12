using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Geometry;

namespace Haugbui.Core.Storage
{
    /// <summary>One player-built pile. <see cref="Refund"/> is the whole stack the piece returns.</summary>
    public sealed class MaterialPile
    {
        public string Id { get; set; } = string.Empty;
        public Vec3 Position { get; set; }
        public ItemStack Refund { get; set; }
    }

    /// <summary>A pile is one recoverable stack of one material, the size of that item's own stack.</summary>
    public static class PileRules
    {
        public static bool IsPile(bool playerBuilt, int requirementCount, bool recoverable, bool material, int amount, int maxStack) =>
            playerBuilt && requirementCount == 1 && recoverable && material && maxStack > 1 && amount == maxStack;
    }

    /// <summary>Opens a pile into the bag. What does not fit is <see cref="PlannedTake.Dropped"/>.</summary>
    public static class PileSource
    {
        public static MaterialPile Nearest(IEnumerable<MaterialPile> piles, string prefab, bool? cheated, Vec3 from, HashSet<string> skip)
        {
            MaterialPile best = null;
            int bestClean = 2;
            float bestDistance = float.MaxValue;
            string bestId = null;
            if (piles == null) return null;
            foreach (MaterialPile pile in piles)
            {
                ItemStack refund = pile?.Refund;
                if (refund == null || refund.Prefab != prefab || refund.Amount <= 0) continue;
                if (skip != null && skip.Contains(pile.Id)) continue;
                if (cheated != null && refund.Cheated != cheated.Value) continue;
                int clean = refund.Cheated ? 1 : 0;
                float distance = Vec3.Distance(from, pile.Position);
                bool closer = best == null || clean < bestClean || clean == bestClean && (distance < bestDistance
                    || distance == bestDistance && string.CompareOrdinal(pile.Id, bestId) < 0);
                if (!closer) continue;
                best = pile;
                bestClean = clean;
                bestDistance = distance;
                bestId = pile.Id;
            }
            return best;
        }

        /// <summary>Puts the refund into <paramref name="carry"/>. Nothing is opened when the bag cannot take a single unit.</summary>
        public static PlannedTake? Open(IEnumerable<MaterialPile> piles, string prefab, bool? cheated, CarriedInventory carry,
            Vec3 from, HashSet<string> skip)
        {
            MaterialPile pile = Nearest(piles, prefab, cheated, from, skip);
            if (pile == null) return null;
            skip?.Add(pile.Id);
            int fit = carry.Add(pile.Refund.Copy(pile.Refund.Amount));
            if (fit <= 0) return null;
            return new PlannedTake(pile.Id, pile.Refund.Copy(fit), pile.Refund.Amount - fit);
        }
    }
}
