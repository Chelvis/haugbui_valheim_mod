using System.Collections.Generic;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Tribute;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Structures;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>HB-T-039/102: status, toggles and tribute of one tomb. Opened with alt-interact on the tomb.</summary>
    internal static class TombPanel
    {
        private const float Width = 420f;
        private const float Height = 690f;
        private const int OfferColumns = 3;

        private static readonly (TombToggles Toggle, string Label)[] Toggles =
        {
            (TombToggles.GroundItems, "$haugbui_panel_ground"),
            (TombToggles.Harvesting, "$haugbui_panel_harvest"),
            (TombToggles.Combat, "$haugbui_panel_combat"),
            (TombToggles.Production, "$haugbui_panel_production"),
            (TombToggles.Resting, "$haugbui_panel_rest"),
        };

        private static GameObject _root;
        private static Text _title;
        private static Text _status;
        private static Text _tribute;
        private static string _builtFor;
        private static readonly List<(string Prefab, Button Button)> Offers = new List<(string, Button)>();
        private static readonly Dictionary<TombToggles, Toggle> ToggleControls = new Dictionary<TombToggles, Toggle>();
        private static readonly Dictionary<TombToggles, Text> ToggleLabels = new Dictionary<TombToggles, Text>();
        private static TombController _tomb;
        private static bool _refreshing;
        private static float _openedAt;

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static TombController Current => IsOpen ? _tomb : null;
        internal static IReadOnlyList<(string Prefab, Button Button)> OfferButtons => Offers;
        internal static string TributeText => _tribute != null ? _tribute.text : string.Empty;

        public static void Open(TombController tomb)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || tomb == null) return;
            string values = TributeTable.ValuesToConfig(HaugbuiPlugin.Instance.Settings.TributeTable.Values);
            if (_root != null && _builtFor != values)
            {
                if (_root.activeSelf) GUIManager.BlockInput(false);
                Object.Destroy(_root);
                _root = null;
            }
            if (_root == null) Build();
            _builtFor = values;
            _tomb = tomb;
            _openedAt = Time.time;
            Refresh();
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                GUIManager.BlockInput(true);
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            _root.SetActive(false);
            GUIManager.BlockInput(false);
            _tomb = null;
        }

        /// <summary>Called every frame by the plugin.</summary>
        public static void Update()
        {
            if (!IsOpen) return;
            if (_tomb == null || Player.m_localPlayer == null
                || Input.GetKeyDown(KeyCode.Escape)
                || (Time.time - _openedAt > 0.2f && Input.GetKeyDown(KeyCode.E)))
            {
                Close();
                return;
            }
            if (Time.frameCount % 15 == 0) Refresh();
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            _title = gui.CreateText(Localization.instance.Localize("$haugbui_piece_tomb"), _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -35f), gui.AveriaSerifBold, 26, gui.ValheimOrange, true, Color.black, Width - 40f, 40f, false).GetComponent<Text>();
            _status = gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -140f),
                gui.AveriaSerifBold, 17, Color.white, true, Color.black, Width - 50f, 170f, false).GetComponent<Text>();

            for (int i = 0; i < Toggles.Length; i++)
            {
                (TombToggles flag, string label) = Toggles[i];
                GameObject go = gui.CreateToggle(_root.transform, 28f, 28f);
                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(45f, -260f - i * 38f);
                Toggle toggle = go.GetComponent<Toggle>();
                toggle.onValueChanged.AddListener(on => OnToggle(flag, on));
                ToggleControls[flag] = toggle;

                Text text = gui.CreateText(Localization.instance.Localize(label), _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(70f + (Width - 110f) / 2f, -260f - i * 38f), gui.AveriaSerifBold, 17, Color.white, true, Color.black,
                    Width - 110f, 30f, false).GetComponent<Text>();
                ToggleLabels[flag] = text;
            }

            _tribute = gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -475f),
                gui.AveriaSerifBold, 17, Color.white, true, Color.black, Width - 50f, 50f, false).GetComponent<Text>();
            Offers.Clear();
            IReadOnlyList<KeyValuePair<string, int>> valuables = HaugbuiPlugin.Instance.Settings.TributeTable.Values;
            for (int i = 0; i < valuables.Count; i++)
            {
                string prefab = valuables[i].Key;
                float x = (i % OfferColumns - (OfferColumns - 1) / 2f) * 128f;
                float y = -530f - i / OfferColumns * 42f;
                GameObject button = gui.CreateButton(OfferLabel(prefab, valuables[i].Value), _root.transform, new Vector2(0.5f, 1f),
                    new Vector2(0.5f, 1f), new Vector2(x, y), 122f, 36f);
                button.GetComponent<Button>().onClick.AddListener(() => Offer(prefab));
                Offers.Add((prefab, button.GetComponent<Button>()));
            }

            GameObject storage = gui.CreateButton(Localization.instance.Localize("$haugbui_panel_open_storage"), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-95f, 40f), 170f, 40f);
            storage.GetComponent<Button>().onClick.AddListener(OpenStorage);
            GameObject close = gui.CreateButton(Localization.instance.Localize("$haugbui_panel_close"), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(95f, 40f), 170f, 40f);
            close.GetComponent<Button>().onClick.AddListener(Close);
            _root.SetActive(false);
        }

        private static void Refresh()
        {
            if (_tomb == null || _tomb.State == null) return;
            _refreshing = true;
            HaugbuiStructure structure = _tomb.Structure;
            TombState state = _tomb.State;
            HaugbuiAttributes attributes = _tomb.Attributes;
            float health = _tomb.Creature != null ? _tomb.Creature.Character.GetHealth() : _tomb.EffectiveHealth(attributes.MaxHealth);

            _title.text = _tomb.ServantName;
            _tribute.text = TributeLine(_tomb);
            Inventory inventory = Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory() : null;
            foreach ((string prefab, Button button) in Offers)
                button.interactable = inventory != null && inventory.GetAllItems().Exists(i => ItemBridge.PrefabName(i) == prefab);

            _status.text = Localization.instance.Localize(
                $"{structure.LevelLine()}\n" +
                $"$haugbui_panel_health {Mathf.CeilToInt(health)}/{attributes.MaxHealth:0}\n" +
                $"$haugbui_panel_state {Localization.instance.Localize(_tomb.StatusLine())}\n" +
                $"$haugbui_panel_carried {_tomb.Carried.Weight:0.#}/{attributes.CarryCapacity:0}" +
                (_tomb.Overflow.Count > 0 ? "  $haugbui_panel_overflow" : "") +
                $"\n$haugbui_panel_activity {_tomb.Worker.Activity ?? "$haugbui_activity_none"}") +
                (_tomb.Worker.PartiallyLoaded
                    ? "\n<color=#e0b050>" + Localization.instance.Localize("$haugbui_panel_partially_loaded",
                        Mathf.FloorToInt(_tomb.Worker.Coverage * 100f).ToString()) + "</color>"
                    : "");

            foreach ((TombToggles flag, string label) in Toggles)
            {
                string requirement = Requirement(flag, structure);
                Toggle toggle = ToggleControls[flag];
                toggle.isOn = state.Has(flag);
                toggle.interactable = requirement == null || state.Has(flag);
                ToggleLabels[flag].text = Localization.instance.Localize(label) +
                                          (requirement != null ? $" <color=#999999>({Localization.instance.Localize(requirement)})</color>" : "");
            }
            _refreshing = false;
        }

        /// <summary>Balance, cost per shift and the shift timer (HB-169).</summary>
        internal static string TributeLine(TombController tomb)
        {
            ShiftTimer timer = tomb.Timer;
            string time = double.IsNaN(timer.Seconds) ? "" :
                "\n" + Localization.instance.Localize(timer.InShift ? "$haugbui_panel_shift_left" : "$haugbui_panel_next_shift",
                    PresencePolicy.FormatCountdown(timer.Seconds));
            if (!TombController.TributeEnabled) return time.TrimStart('\n');
            return Localization.instance.Localize("$haugbui_panel_tribute", tomb.State.Balance.ToString(), tomb.ShiftCost.ToString()) + time;
        }

        private static string OfferLabel(string prefab, int value)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            string name = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : prefab;
            return Localization.instance.Localize(name) + " +" + value;
        }

        private static void Offer(string prefab)
        {
            if (_tomb == null) return;
            if (!_tomb.Offer(prefab, Player.m_localPlayer)) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$haugbui_msg_offer_failed");
            Refresh();
        }

        private static string Requirement(TombToggles flag, HaugbuiStructure structure)
        {
            if (flag == TombToggles.Combat && !structure.Progress.Has(TombAbilities.Combat)) return "$haugbui_panel_needs_longsword";
            if (flag == TombToggles.Production && structure.Progress.Level < HaugbuiPlugin.Instance.Settings.ProductionUnlockLevel.Value)
                return Localization.instance.Localize("$haugbui_panel_needs_level", HaugbuiPlugin.Instance.Settings.ProductionUnlockLevel.Value.ToString());
            return null;
        }

        private static void OnToggle(TombToggles flag, bool on)
        {
            if (_refreshing || _tomb == null) return;
            if (flag == TombToggles.Combat && on)
            {
                RequestCombat();
                return;
            }
            if (!_tomb.SetToggle(flag, on, out string refusal) && refusal != null)
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, refusal);
            Refresh();
        }

        private static void RequestCombat()
        {
            TombController tomb = _tomb;
            CombatWarning.RequestEnable(tomb, (on, refusal) =>
            {
                if (!string.IsNullOrEmpty(refusal)) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, refusal);
                if (_root != null && !_root.activeSelf && _tomb == tomb) _root.SetActive(true);
                if (IsOpen) Refresh();
            });
            // The vanilla popup draws under the panel: hide the panel (input stays blocked) until it is answered.
            if (CombatWarning.Pending != null) _root.SetActive(false);
            else Refresh();
        }

        private static void OpenStorage()
        {
            Container container = _tomb != null ? _tomb.GetComponent<Container>() : null;
            Close();
            if (container != null && Player.m_localPlayer != null) container.Interact(Player.m_localPlayer, false, false);
        }
    }
}
