using System;
using HarmonyLib;

namespace Haugbui.UI
{
    /// <summary>
    /// The filter and delivery editors stay open while the player can still take input, so C and V
    /// would toggle walk and auto loot. Shift+F on the tomb would do the same for whatever F is bound to.
    /// </summary>
    [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetButtonDown))]
    internal static class PanelShortcutGuard
    {
        private static bool Prefix(string name, ref bool __result)
        {
            bool editors = ChestFilterPanel.IsOpen || DeliveryPanel.IsOpen;
            bool walkOrLoot = name == "ToggleWalk" || name == "AutoPickup";
            // C walks and V picks up. Shift+C and Shift+V on a chest, or either key while an editor is open, are the list.
            bool listKeys = walkOrLoot && (editors || (PanelInput.ShiftHeld() && ChestListHotkeys.AimedAtList()));
            bool findKey = TombPanel.IsOpen && PanelInput.ShiftHeld() && BoundToKeyboardF(name);
            if (!listKeys && !findKey) return true;
            ZInput.ResetButtonStatus(name);
            __result = false;
            return false;
        }

        private static bool BoundToKeyboardF(string name)
        {
            if (ZInput.instance == null) return false;
            string shown = ZInput.instance.GetBoundKeyString(name, true);
            return string.Equals(shown, "F", StringComparison.OrdinalIgnoreCase);
        }
    }
}
