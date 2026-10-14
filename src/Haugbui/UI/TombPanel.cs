using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Structures;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>HB-T-039: status and toggles of one tomb. Opened with alt-interact on the tomb.</summary>
    internal static class TombPanel
    {
        private const float Width = 420f;
        private const float Height = 530f;

        private static readonly (TombToggles Toggle, string Label)[] Toggles =
        {
            (TombToggles.GroundItems, "$haugbui_panel_ground"),
            (TombToggles.Harvesting, "$haugbui_panel_harvest"),
            (TombToggles.Combat, "$haugbui_panel_combat"),
            (TombToggles.Production, "$haugbui_panel_production"),
            (TombToggles.Resting, "$haugbui_panel_rest"),
        };

        private static GameObject _root;
        private static Text _status;
        private static readonly Dictionary<TombToggles, Toggle> ToggleControls = new Dictionary<TombToggles, Toggle>();
        private static readonly Dictionary<TombToggles, Text> ToggleLabels = new Dictionary<TombToggles, Text>();
        private static TombController _tomb;
        private static bool _refreshing;
        private static float _openedAt;

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static TombController Current => IsOpen ? _tomb : null;

        public static void Open(TombController tomb)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || tomb == null) return;
            if (_root == null) Build();
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
            gui.CreateText(Localization.instance.Localize("$haugbui_piece_tomb"), _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -35f), gui.AveriaSerifBold, 26, gui.ValheimOrange, true, Color.black, Width - 40f, 40f, false);
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
            if (!_tomb.SetToggle(flag, on, out string refusal) && refusal != null)
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, refusal);
            Refresh();
        }

        private static void OpenStorage()
        {
            Container container = _tomb != null ? _tomb.GetComponent<Container>() : null;
            Close();
            if (container != null && Player.m_localPlayer != null) container.Interact(Player.m_localPlayer, false, false);
        }
    }
}
