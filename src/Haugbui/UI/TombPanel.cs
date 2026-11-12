using System.Collections.Generic;
using System.Text;
using Haugbui.Core.Creature;
using Haugbui.Core.Persistence;
using Haugbui.Core.Tribute;
using Haugbui.Core.Progression;
using Haugbui.Core.Structures;
using Haugbui.Creature;
using Haugbui.Structures;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>Status, carried items, toggles and tribute of one tomb. Opened with alt-interact on the tomb.</summary>
    internal static class TombPanel
    {
        private const float Width = 480f;
        private const float Height = 856f;
        private const float Inset = 24f;
        private const float ContentWidth = Width - Inset * 2f;
        private const int OfferRows = 3;

        private static readonly (TombToggles Toggle, string Label)[] Toggles =
        {
            (TombToggles.GroundItems, "$haugbui_panel_ground"),
            (TombToggles.Harvesting, "$haugbui_panel_harvest"),
            (TombToggles.Combat, "$haugbui_panel_combat"),
            (TombToggles.Production, "$haugbui_panel_production"),
            (TombToggles.MaterialPiles, "$haugbui_panel_piles"),
            (TombToggles.Resting, "$haugbui_panel_rest"),
            (TombToggles.AwayMode, "$haugbui_panel_away"),
        };

        private static GameObject _root;
        private static Text _title;
        private static InputField _name;
        private static Text _status;
        private static Text _statusSide;
        private static Text _carried;
        private static Text _tribute;
        private static GameObject _awayTip;
        private static Button _retrieve;
        private static Button _find;
        private static Button _dismiss;
        private static string _builtFor;
        private static readonly List<(string Prefab, Button Button)> Offers = new List<(string, Button)>();
        private static readonly Dictionary<TombToggles, Toggle> ToggleControls = new Dictionary<TombToggles, Toggle>();
        private static readonly Dictionary<TombToggles, Text> ToggleLabels = new Dictionary<TombToggles, Text>();
        private static TombController _tomb;
        private static bool _refreshing;
        private static bool _closing;
        private static float _openedAt;

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static TombController Current => IsOpen ? _tomb : null;
        internal static IReadOnlyList<(string Prefab, Button Button)> OfferButtons => Offers;
        internal static string TributeText => _tribute != null ? _tribute.text : string.Empty;
        internal static string StatusText
        {
            get
            {
                string left = _status != null ? _status.text : string.Empty;
                string right = _statusSide != null ? _statusSide.text : string.Empty;
                if (right.Length == 0) return left;
                return left.Length == 0 ? right : left + "\n" + right;
            }
        }
        internal static string CarriedText => _carried != null ? _carried.text : string.Empty;
        internal static string TitleText => _title != null ? _title.text : string.Empty;
        internal static Button Dismiss => _dismiss;
        internal static Button Find => _find;

        public static void Open(TombController tomb)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || tomb == null) return;
            string values = TributeTable.ValuesToConfig(HaugbuiPlugin.Instance.Settings.TributeTable.Values);
            if (_root != null && _builtFor != values)
            {
                if (_root.activeSelf) PanelInput.Released();
                Object.Destroy(_root);
                _root = null;
            }
            if (_root == null) Build();
            _builtFor = values;
            _tomb = tomb;
            _openedAt = Time.time;
            if (_awayTip != null) _awayTip.SetActive(false);
            Refresh();
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                PanelInput.Acquired();
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            // Deactivating the name field and the toggles fires their callbacks. A toggle callback would turn a
            // setting off, or open the combat confirmation and show the panel again, while this close is in progress.
            _closing = true;
            PanelInput.ClearSelection();
            _root.SetActive(false);
            PanelInput.Released();
            _tomb = null;
            _closing = false;
        }

        /// <summary>Called every frame by the plugin.</summary>
        public static void Update()
        {
            if (!IsOpen) return;
            if (_tomb == null || Player.m_localPlayer == null || PanelInput.ShouldClose(_openedAt))
            {
                Close();
                return;
            }
            if (Time.frameCount % 15 == 0) Refresh();
        }

        /// <summary>Same path as leaving the name field.</summary>
        internal static bool CommitName(string input)
        {
            if (_tomb == null) return false;
            bool saved = _tomb.Rename(input);
            if (_closing) return saved;
            if (_name != null && !_name.isFocused && _tomb != null) _name.text = _tomb.ServantName;
            Refresh();
            return saved;
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            _title = PanelChrome.TopLeft(gui, _root.transform, "", Inset, 20f, ContentWidth - 56f, 34f, 24);
            _title.horizontalOverflow = HorizontalWrapMode.Wrap;
            _title.verticalOverflow = VerticalWrapMode.Truncate;
            const float nameTop = 62f;
            const float nameHeight = 32f;
            _name = gui.CreateInputField(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Inset + ContentWidth / 2f, -(nameTop + nameHeight / 2f)),
                InputField.ContentType.Standard, Localization.instance.Localize("$haugbui_panel_rename"), 18, ContentWidth, nameHeight).GetComponent<InputField>();
            var nameRect = (RectTransform)_name.transform;
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.anchoredPosition = new Vector2(Inset + ContentWidth / 2f, -(nameTop + nameHeight / 2f));
            _name.characterLimit = ServantSlots.MaxNameLength;
            _name.onEndEdit.AddListener(text => CommitName(text));
            if (_name.textComponent != null) _name.textComponent.alignment = TextAnchor.MiddleLeft;
            if (_name.placeholder is Text nameHint) nameHint.alignment = TextAnchor.MiddleLeft;

            float column = (ContentWidth - 16f) / 2f;
            _status = Block(gui, Inset, 108f, column, 112f);
            _statusSide = Block(gui, Inset + column + 16f, 108f, column, 112f);
            _carried = Block(gui, Inset, 232f, ContentWidth, 88f);

            const float toggleTop = 336f;
            const float toggleStep = 34f;
            for (int i = 0; i < Toggles.Length; i++)
            {
                (TombToggles flag, string label) = Toggles[i];
                float y = -(toggleTop + i * toggleStep);
                GameObject go = gui.CreateToggle(_root.transform, 26f, 26f);
                RectTransform rect = (RectTransform)go.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(Inset, y);
                Toggle toggle = go.GetComponent<Toggle>();
                toggle.onValueChanged.AddListener(on => OnToggle(flag, on));
                ToggleControls[flag] = toggle;

                Text text = gui.CreateText(Localization.instance.Localize(label), _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    Vector2.zero, gui.AveriaSerifBold, 16, Color.white, true, Color.black, ContentWidth - 36f, 28f, false).GetComponent<Text>();
                var labelRect = (RectTransform)text.transform;
                labelRect.pivot = new Vector2(0f, 0.5f);
                labelRect.anchoredPosition = new Vector2(Inset + 36f, y);
                text.alignment = TextAnchor.MiddleLeft;
                ToggleLabels[flag] = text;
                if (flag == TombToggles.AwayMode) AddAwayWarning(gui, text, y);
            }

            _tribute = Block(gui, Inset, 568f, ContentWidth, 44f);
            Offers.Clear();
            IReadOnlyList<KeyValuePair<string, int>> valuables = HaugbuiPlugin.Instance.Settings.TributeTable.Values;
            int columns = (valuables.Count + OfferRows - 1) / OfferRows;
            if (columns < 1) columns = 1;
            const float offerGap = 12f;
            float buttonWidth = (ContentWidth - offerGap * (columns - 1)) / columns;
            const float offerTop = 628f;
            for (int i = 0; i < valuables.Count; i++)
            {
                string prefab = valuables[i].Key;
                int columnIndex = i % columns;
                int row = i / columns;
                float x = Inset + buttonWidth / 2f + columnIndex * (buttonWidth + offerGap);
                float y = -(offerTop + row * 40f);
                GameObject button = gui.CreateButton(OfferLabel(prefab, valuables[i].Value), _root.transform, new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(x, y), buttonWidth, 34f);
                button.GetComponent<Button>().onClick.AddListener(() => Offer(prefab));
                Offers.Add((prefab, button.GetComponent<Button>()));
            }

            _find = BottomButton(gui, "$haugbui_panel_find", Inset, ContentWidth, FindServant, 76f).GetComponent<Button>();
            float half = (ContentWidth - 12f) / 2f;
            _retrieve = BottomButton(gui, "$haugbui_panel_retrieve", Inset, half, Retrieve).GetComponent<Button>();
            BottomButton(gui, "$haugbui_panel_open_storage", Inset + half + 12f, half, OpenStorage);
            _dismiss = PanelChrome.CornerDismiss(gui, _root.transform, Close);
            _root.SetActive(false);
        }

        private static Text Block(GUIManager gui, float left, float top, float width, float height)
        {
            GameObject go = gui.CreateText("", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, gui.AveriaSerifBold, 16, Color.white, true, Color.black, width, height, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(left, -top);
            Text text = go.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static GameObject BottomButton(GUIManager gui, string label, float left, float width, UnityEngine.Events.UnityAction action, float y = 32f)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(label), _root.transform,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(left + width / 2f, y), width, 36f);
            button.GetComponent<Button>().onClick.AddListener(action);
            return button;
        }

        private static void Refresh()
        {
            if (_tomb == null || _tomb.State == null) return;
            _refreshing = true;
            HaugbuiStructure structure = _tomb.Structure;
            TombState state = _tomb.State;
            HaugbuiAttributes attributes = _tomb.Attributes;
            float health = _tomb.Creature != null ? _tomb.Creature.Character.GetHealth() : _tomb.EffectiveHealth(attributes.MaxHealth);
            double wait = _tomb.WaitRemaining;
            string cooldown = wait > 0
                ? PresencePolicy.FormatCountdown(wait)
                : Localization.instance.Localize("$haugbui_hover_ready");

            _title.text = _tomb.ServantName;
            if (_name != null && !_name.isFocused) _name.text = _tomb.ServantName;
            _tribute.text = TributeLine(_tomb);
            if (_retrieve != null) _retrieve.interactable = _tomb.Overflow.Count > 0;
            Inventory inventory = Player.m_localPlayer != null ? Player.m_localPlayer.GetInventory() : null;
            foreach ((string prefab, Button button) in Offers)
                button.interactable = Held(inventory, prefab) >= OfferCount(prefab);

            var left = new List<string>
            {
                structure.LevelLine(),
                Localization.instance.Localize("$haugbui_panel_health") + $" {Mathf.CeilToInt(health)}/{attributes.MaxHealth:0}",
                Localization.instance.Localize("$haugbui_panel_state") + " " + Localization.instance.Localize(_tomb.StatusLine()),
                Localization.instance.Localize("$haugbui_panel_carried") + $" {_tomb.Carried.Weight:0.#}/{attributes.CarryCapacity:0}"
                    + (_tomb.Overflow.Count > 0 ? "  " + Localization.instance.Localize("$haugbui_panel_overflow") : ""),
            };
            _status.text = string.Join("\n", left);
            string coverage = _tomb.Worker.PartiallyLoaded
                ? "<color=#e0b050>" + Localization.instance.Localize("$haugbui_panel_partially_loaded",
                    Mathf.FloorToInt(_tomb.Worker.Coverage * 100f).ToString()) + "</color>\n"
                : "";
            _carried.text = coverage + CarriedGrid(_tomb);
            _statusSide.text = string.Join("\n", new[]
            {
                Localization.instance.Localize("$haugbui_panel_speed", attributes.MaxSpeed.ToString("0.#")),
                Localization.instance.Localize("$haugbui_panel_radius", attributes.Radius.ToString("0")),
                Localization.instance.Localize("$haugbui_panel_cooldown", cooldown),
                Localization.instance.Localize("$haugbui_panel_activity") + " " + Localization.instance.Localize(_tomb.Worker.Activity ?? "$haugbui_activity_none"),
            });

            if (_awayTip != null)
            {
                Text tip = _awayTip.GetComponentInChildren<Text>();
                if (tip != null) tip.text = Localization.instance.Localize("$haugbui_panel_away_warn");
            }
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

        private static string CarriedGrid(TombController tomb)
        {
            if (tomb.Carried.IsEmpty) return Localization.instance.Localize("$haugbui_panel_carried_empty");
            var text = new StringBuilder();
            foreach (ItemStack stack in tomb.Carried.Stacks)
            {
                if (text.Length > 0) text.Append('\n');
                string line = Localization.instance.Localize("$haugbui_panel_stack", ItemName(stack.Prefab), stack.Amount.ToString());
                if (stack.Cheated) line += " " + Localization.instance.Localize("$haugbui_panel_cheated");
                text.Append(line);
            }
            return text.ToString();
        }

        private static string ItemName(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            string token = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : prefab;
            return Localization.instance.Localize(token);
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
            if (prefab == TombController.CoinPrefab) return Localization.instance.Localize("$haugbui_offer_coins");
            return ItemName(prefab) + " +" + value;
        }

        private static int OfferCount(string prefab) => prefab == TombController.CoinPrefab ? TombController.CoinBundle : 1;

        private static int Held(Inventory inventory, string prefab)
        {
            if (inventory == null) return 0;
            int held = 0;
            foreach (ItemDrop.ItemData item in inventory.GetAllItems())
                if (ItemBridge.PrefabName(item) == prefab) held += item.m_stack;
            return held;
        }

        private static void Offer(string prefab)
        {
            if (_tomb == null) return;
            if (!_tomb.Offer(prefab, Player.m_localPlayer, OfferCount(prefab)))
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$haugbui_msg_offer_failed");
            Refresh();
        }

        private static void FindServant()
        {
            TombController tomb = _tomb;
            Close();
            ServantFinder.Look(tomb);
        }

        private static void Retrieve()
        {
            if (_tomb == null || Player.m_localPlayer == null) return;
            if (_tomb.Overflow.Count == 0)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$haugbui_msg_overflow_none");
                return;
            }
            int left = _tomb.RetrieveOverflow(Player.m_localPlayer);
            if (left > 0) Player.m_localPlayer.Message(MessageHud.MessageType.Center, "$haugbui_msg_overflow_left");
            Refresh();
        }

        private static void AddAwayWarning(GUIManager gui, Text label, float y)
        {
            var labelRect = (RectTransform)label.transform;
            labelRect.sizeDelta = new Vector2(ContentWidth - 68f, 32f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;

            Text mark = gui.CreateText("\u26a0", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, gui.AveriaSerifBold, 18, new Color(0.95f, 0.72f, 0.25f), true, Color.black, 28f, 28f, false).GetComponent<Text>();
            var markRect = (RectTransform)mark.transform;
            markRect.pivot = new Vector2(0.5f, 0.5f);
            markRect.anchoredPosition = new Vector2(Width - Inset - 16f, y);
            mark.alignment = TextAnchor.MiddleCenter;
            mark.raycastTarget = true;

            var tip = new GameObject("AwayWarning", typeof(RectTransform), typeof(Image));
            tip.transform.SetParent(_root.transform, false);
            var tipRect = (RectTransform)tip.transform;
            tipRect.anchorMin = tipRect.anchorMax = new Vector2(0f, 1f);
            tipRect.pivot = new Vector2(0f, 1f);
            tipRect.sizeDelta = new Vector2(ContentWidth, 68f);
            tipRect.anchoredPosition = new Vector2(Inset, y - 22f);
            Image plate = tip.GetComponent<Image>();
            plate.color = new Color(0.08f, 0.07f, 0.05f, 0.95f);
            plate.raycastTarget = false;
            Image panel = _root.GetComponent<Image>();
            if (panel != null) plate.sprite = panel.sprite;

            Text tipText = gui.CreateText(Localization.instance.Localize("$haugbui_panel_away_warn"), tip.transform,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -4f), gui.AveriaSerifBold, 14, Color.white,
                true, Color.black, ContentWidth - 16f, 60f, false).GetComponent<Text>();
            var tipTextRect = (RectTransform)tipText.transform;
            tipTextRect.pivot = new Vector2(0f, 1f);
            tipTextRect.anchoredPosition = new Vector2(8f, -4f);
            tipText.alignment = TextAnchor.UpperLeft;
            tipText.horizontalOverflow = HorizontalWrapMode.Wrap;
            tipText.raycastTarget = false;
            tip.SetActive(false);
            _awayTip = tip;

            AwayHint hint = mark.gameObject.AddComponent<AwayHint>();
            hint.Tip = tip;
        }

        private sealed class AwayHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public GameObject Tip;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (Tip == null) return;
                Tip.SetActive(true);
                Tip.transform.SetAsLastSibling();
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (Tip != null) Tip.SetActive(false);
            }
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
            if (_closing || _refreshing || _tomb == null) return;
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
