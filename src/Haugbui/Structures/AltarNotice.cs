namespace Haugbui.Structures
{
    /// <summary>Shown when the Altar of Darkness binds to a tomb. Daylight work starts immediately; no confirmation.</summary>
    internal static class AltarNotice
    {
        public static void Show()
        {
            if (Player.m_localPlayer == null) return;
            UnifiedPopup.Push(new WarningPopup("$haugbui_altar_warning_title", "$haugbui_altar_warning_text", UnifiedPopup.Pop));
        }
    }
}
