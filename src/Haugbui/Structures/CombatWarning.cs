namespace Haugbui.Structures
{
    /// <summary>HB-026: shown when the Longsword binds to a tomb. Combat itself stays off until enabled in the tomb panel.</summary>
    internal static class CombatWarning
    {
        public static void Show()
        {
            if (Player.m_localPlayer == null) return;
            UnifiedPopup.Push(new WarningPopup("$haugbui_combat_warning_title", "$haugbui_combat_warning_text", UnifiedPopup.Pop));
        }
    }
}
