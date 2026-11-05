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

        public static void ClearSelection()
        {
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
        }

        public static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        public static bool ShiftHeld() => Down(KeyCode.LeftShift, held: true) || Down(KeyCode.RightShift, held: true);

        public static bool CopyPressed() => !Typing() && ShiftHeld() && Down(KeyCode.C, held: false);

        public static bool PastePressed() => !Typing() && ShiftHeld() && Down(KeyCode.V, held: false);

        public static bool FindPressed() => !Typing() && ShiftHeld() && Down(KeyCode.F, held: false);

        /// <summary>Valheim reads the keyboard through ZInput. Unity's Input does not see these keys.</summary>
        private static bool Down(KeyCode key, bool held) =>
            held ? ZInput.GetKey(key, false) : ZInput.GetKeyDown(key, false);

        /// <summary>Esc and the gamepad back button always close. E closes unless a text field is focused.</summary>
        public static bool ShouldClose(float openedAt)
        {
            if (ZInput.GetKeyDown(KeyCode.Escape, false) || ZInput.GetButtonDown("JoyButtonB"))
            {
                ZInput.ResetButtonStatus("JoyButtonB");
                return true;
            }
            return Time.time - openedAt > 0.2f && !Typing() && ZInput.GetKeyDown(KeyCode.E, false);
        }
    }
}
