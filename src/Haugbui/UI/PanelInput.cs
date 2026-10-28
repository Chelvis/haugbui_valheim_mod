using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>
    /// One shared input block for every Haugbui panel, so closing one cannot leave the block held,
    /// and a text field keeps E (and movement, via the block) from acting as play input.
    /// </summary>
    internal static class PanelInput
    {
        private static int _held;

        public static void Acquired()
        {
            if (_held++ == 0) Jotunn.Managers.GUIManager.BlockInput(true);
        }

        public static void Released()
        {
            if (_held == 0) return;
            if (--_held == 0) Jotunn.Managers.GUIManager.BlockInput(false);
        }

        public static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        /// <summary>Esc and the gamepad back button always close. E closes unless a text field is focused.</summary>
        public static bool ShouldClose(float openedAt)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
            {
                ZInput.ResetButtonStatus("JoyButtonB");
                return true;
            }
            return Time.time - openedAt > 0.2f && !Typing() && Input.GetKeyDown(KeyCode.E);
        }
    }
}
