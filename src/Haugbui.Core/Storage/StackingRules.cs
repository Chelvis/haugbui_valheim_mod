using Haugbui.Core.Creature;

namespace Haugbui.Core.Storage
{
    /// <summary>
    /// HB-061 / HB-T-109: how a moved item joins a stack already in a chest. Strict (default): identical item data only.
    /// Vanilla: any stack of the same item, quality and world level, with the game's result (a cheated item makes the
    /// stack cheated; the stack keeps its own custom data and crafter). Neither ever clears a cheated flag. Sources
    /// always give identical items, and the carried inventory always stays strict, so plans find what they took.
    /// </summary>
    public static class StackingRules
    {
        /// <summary>Config StrictItemData; set by the plugin at load and when the setting changes.</summary>
        public static bool StrictItemData { get; set; } = true;

        public static bool CanJoin(ItemStack stack, ItemStack incoming) =>
            StrictItemData ? stack.StacksWith(incoming) : stack.SameKind(incoming);

        public static void Join(ItemStack stack, ItemStack incoming, int amount)
        {
            stack.Amount += amount;
            if (incoming.Cheated) stack.Cheated = true;
        }
    }
}
