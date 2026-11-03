using HarmonyLib;
using Haugbui.Structures;
using Jotunn.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>
    /// Edit filters on a vanilla chest window, Edit requests on a delivery chest window.
    /// Cloned from the chest's own button so it matches that window. The inventory closes first:
    /// the panel then owns the input block, and closing it does not leave movement blocked.
    /// </summary>
    internal static class ChestEditButtons
    {
        private static Button _button;
        private static TMP_Text _label;
        private static Container _container;

        internal static bool Visible => _button != null && _button.gameObject.activeInHierarchy;
        internal static string Label => _label != null ? _label.text : "";

        internal static void Bind(Container container) => _container = container;

        internal static void Sync()
        {
            InventoryGui gui = InventoryGui.instance;
            if (gui == null || gui.m_container == null || gui.m_takeAllButton == null) return;
            if (_button == null) Create(gui);
            if (_button == null) return;
            Place(gui);
            bool eligible = Eligible(_container);
            if (eligible && _label != null) _label.text = Localization.instance.Localize(LabelOf(_container));
            _button.gameObject.SetActive(gui.m_container.gameObject.activeInHierarchy && eligible);
        }

        /// <summary>Same path as the chest-window button.</summary>
        internal static void Press()
        {
            Container container = _container;
            if (container == null || !Eligible(container)) return;
            DeliveryChest delivery = container.GetComponent<DeliveryChest>();
            if (InventoryGui.instance != null) InventoryGui.instance.Hide();
            if (delivery != null) DeliveryPanel.Open(delivery);
            else ChestFilterPanel.Open(container);
        }

        private static void Create(InventoryGui gui)
        {
            _button = Object.Instantiate(gui.m_takeAllButton, gui.m_container);
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(Press);
            _label = _button.GetComponentInChildren<TMP_Text>(true);
            Place(gui);
            _button.gameObject.SetActive(false);
        }

        /// <summary>
        /// Just under the chest panel's bottom-right. A fixed offset inside the panel lands on the last row of a tall chest.
        /// </summary>
        private static void Place(InventoryGui gui)
        {
            RectTransform rect = _button.GetComponent<RectTransform>();
            rect.SetParent(gui.m_container, false);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(200f, 42f);
            rect.anchoredPosition = new Vector2(-18f, -8f);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
        }

        private static bool Eligible(Container container)
        {
            if (container == null) return false;
            if (container.GetComponent<DeliveryChest>() != null) return Allowed(container);
            if (container.GetComponent<HaugbuiStructure>() != null) return false;
            if (container.GetComponent<Piece>() == null) return false;
            return Allowed(container);
        }

        private static bool Allowed(Container container)
        {
            if (container.m_checkGuardStone && !PrivateArea.CheckAccess(container.transform.position, 0f, flash: false)) return false;
            long id = Game.instance != null && Game.instance.GetPlayerProfile() != null
                ? Game.instance.GetPlayerProfile().GetPlayerID()
                : 0L;
            return container.CheckAccess(id);
        }

        private static string LabelOf(Container container) =>
            container.GetComponent<DeliveryChest>() != null ? "$haugbui_delivery_edit" : "$haugbui_filter_edit";
    }

    [HarmonyPatch]
    internal static class ChestEditButtonPatches
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static void Shown(Container container) => ChestEditButtons.Bind(container);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPatch(typeof(InventoryGui), "CloseContainer")]
        private static void Hidden() => ChestEditButtons.Bind(null);

        [HarmonyPostfix]
        [HarmonyPatch(typeof(InventoryGui), "UpdateContainer")]
        private static void Updated() => ChestEditButtons.Sync();
    }
}
