using System;
using Haugbui.Core.Persistence;
using Haugbui.Creature;

namespace Haugbui.Structures
{
    /// <summary>
    /// HB-026: shown when the Longsword binds to a tomb, and as a yes/no confirmation the first time combat is enabled.
    /// Combat itself stays off until enabled in the tomb panel.
    /// </summary>
    internal static class CombatWarning
    {
        /// <summary>The confirmation on screen, if any (the self-test answers it through its callbacks).</summary>
        internal static YesNoPopup Pending { get; private set; }

        public static void Show()
        {
            if (Player.m_localPlayer == null) return;
            UnifiedPopup.Push(new WarningPopup("$haugbui_combat_warning_title", "$haugbui_combat_warning_text", UnifiedPopup.Pop));
        }

        /// <summary>
        /// Turns combat on, first asking for confirmation while the warning was never confirmed (combat_ack). Cancel
        /// leaves combat off and unacknowledged.
        /// </summary>
        /// <param name="done">Whether combat is on now, and a refusal message to show (or null).</param>
        public static void RequestEnable(TombController tomb, Action<bool, string> done)
        {
            if (tomb.SetToggle(TombToggles.Combat, true, out string refusal))
            {
                done?.Invoke(true, null);
                return;
            }
            if (refusal != TombController.CombatNeedsConfirmation)
            {
                done?.Invoke(false, refusal);
                return;
            }
            if (Pending != null && !UnifiedPopup.IsVisible()) Pending = null;
            if (Pending != null || !UnifiedPopup.IsAvailable())
            {
                done?.Invoke(false, null);
                return;
            }
            Pending = new YesNoPopup("$haugbui_combat_warning_title", "$haugbui_combat_confirm_text",
                () => Answer(tomb, true, done), () => Answer(tomb, false, done));
            UnifiedPopup.Push(Pending);
        }

        private static void Answer(TombController tomb, bool confirmed, Action<bool, string> done)
        {
            if (Pending == null) return;
            Pending = null;
            UnifiedPopup.Pop();
            bool on = confirmed && tomb != null && tomb.AcknowledgeCombat() && tomb.SetToggle(TombToggles.Combat, true, out _);
            done?.Invoke(on, null);
        }
    }
}
