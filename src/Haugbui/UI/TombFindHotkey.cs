using Haugbui.Creature;
using UnityEngine;

namespace Haugbui.UI
{
    /// <summary>Shift+F while looking at the tomb. The open tomb panel does not take this key.</summary>
    internal static class TombFindHotkey
    {
        public static string HoverLine() => "\n[<color=yellow><b>Shift+F</b></color>] $haugbui_panel_find";

        public static void Update()
        {
            if (TombPanel.IsOpen || !PanelInput.FindPressed()) return;
            TombController tomb = Aimed();
            if (tomb != null) ServantFinder.Look(tomb);
        }

        public static bool AimedAtTomb() => Aimed() != null;

        private static TombController Aimed()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !player.TakeInput()) return null;
            GameObject hover = player.GetHoverObject();
            return hover != null ? hover.GetComponentInParent<TombController>() : null;
        }
    }
}
