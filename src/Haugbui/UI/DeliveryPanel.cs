using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Structures;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>HB-T-052: request lines of one Delivery Chest. Opened with alt-interact on the chest.</summary>
    internal static class DeliveryPanel
    {
        private const float Width = 560f;
        private const float Height = 620f;
        private const float RowTop = -85f;
        private const float RowStep = 36f;

        private static GameObject _root;
        private static readonly List<Text> RowTexts = new List<Text>();
        private static readonly List<GameObject> RowButtons = new List<GameObject>();
        private static InputField _filter;
        private static Dropdown _items;
        private static InputField _amount;
        private static Text _hint;
        private static DeliveryChest _chest;
        private static float _openedAt;
        private static readonly List<(string Prefab, string Name)> Known = new List<(string, string)>();
        private static readonly List<(string Prefab, string Name)> Shown = new List<(string, string)>();

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static DeliveryChest Current => IsOpen ? _chest : null;

        public static void Open(DeliveryChest chest)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || chest == null) return;
            if (_root == null) Build();
            _chest = chest;
            _openedAt = Time.time;
            LoadKnownItems();
            _filter.text = "";
            _amount.text = "";
            ApplyFilter();
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
            _chest = null;
        }

        /// <summary>Called every frame by the plugin.</summary>
        public static void Update()
        {
            if (!IsOpen) return;
            if (_chest == null || Player.m_localPlayer == null || Input.GetKeyDown(KeyCode.Escape)
                || (Time.time - _openedAt > 0.2f && !Typing() && Input.GetKeyDown(KeyCode.E)))
            {
                Close();
                return;
            }
            if (Time.frameCount % 15 == 0) Refresh();
        }

        /// <summary>Self-test hook: the same path as the Request button.</summary>
        internal static RequestEditResult? Submit(string prefab, int amount)
        {
            if (_chest == null) return null;
            RequestEditResult result = RequestEditResult.Ok;
            bool saved = _chest.Edit(ledger => (result = ledger.Request(prefab, amount)) == RequestEditResult.Ok);
            Refresh();
            return saved || result != RequestEditResult.Ok ? result : (RequestEditResult?)null;
        }

        internal static IReadOnlyList<(string Prefab, string Name)> KnownItems => Known;

        private static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField field = selected != null ? selected.GetComponent<InputField>() : null;
            return field != null && field.isFocused;
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            gui.CreateText(Localization.instance.Localize("$haugbui_piece_delivery"), _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -35f), gui.AveriaSerifBold, 26, gui.ValheimOrange, true, Color.black, Width - 40f, 40f, false);

            for (int i = 0; i < DeliveryLedger.MaxLines; i++)
            {
                int index = i;
                float y = RowTop - i * RowStep;
                Text text = gui.CreateText("", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f + 170f, y),
                    gui.AveriaSerifBold, 16, Color.white, true, Color.black, 340f, 30f, false).GetComponent<Text>();
                RowTexts.Add(text);
                GameObject edit = gui.CreateButton(Localization.instance.Localize("$haugbui_delivery_edit_line"), _root.transform,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(420f, y), 80f, 30f);
                edit.GetComponent<Button>().onClick.AddListener(() => EditLine(index));
                GameObject cancel = gui.CreateButton("X", _root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(500f, y), 40f, 30f);
                cancel.GetComponent<Button>().onClick.AddListener(() => CancelLine(index));
                RowButtons.Add(edit);
                RowButtons.Add(cancel);
            }

            float editorY = RowTop - DeliveryLedger.MaxLines * RowStep - 20f;
            _filter = gui.CreateInputField(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(100f, editorY),
                InputField.ContentType.Standard, Localization.instance.Localize("$haugbui_delivery_search"), 16, 140f, 32f).GetComponent<InputField>();
            _filter.onValueChanged.AddListener(_ => ApplyFilter());
            _items = gui.CreateDropDown(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(295f, editorY), 16, 230f, 32f)
                .GetComponent<Dropdown>();
            _amount = gui.CreateInputField(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(475f, editorY),
                InputField.ContentType.IntegerNumber, Localization.instance.Localize("$haugbui_delivery_amount"), 16, 100f, 32f).GetComponent<InputField>();
            _hint = gui.CreateText("", _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0, 95f),
                gui.AveriaSerifBold, 15, new Color(0.9f, 0.75f, 0.4f), true, Color.black, Width - 60f, 28f, false).GetComponent<Text>();

            Button(gui, "$haugbui_delivery_request", -180f, OnRequest);
            Button(gui, "$haugbui_delivery_clear", 0f, OnClearCompleted);
            Button(gui, "$haugbui_panel_close", 180f, Close);
            _root.SetActive(false);
        }

        private static void Button(GUIManager gui, string label, float x, Action action)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(label), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 45f), 165f, 40f);
            button.GetComponent<Button>().onClick.AddListener(() => action());
        }

        /// <summary>HB-082: transportable items the player has discovered; equipment never appears.</summary>
        private static void LoadKnownItems()
        {
            Known.Clear();
            Player player = Player.m_localPlayer;
            if (ObjectDB.instance == null || player == null) return;
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || !ItemBridge.IsTransportable(drop.m_itemData)) continue;
                if (!player.IsMaterialKnown(drop.m_itemData.m_shared.m_name)) continue;
                Known.Add((prefab.name, Localization.instance.Localize(drop.m_itemData.m_shared.m_name)));
            }
            Known.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        private static void ApplyFilter()
        {
            string filter = _filter.text?.Trim() ?? "";
            string selected = Selected();
            Shown.Clear();
            Shown.AddRange(Known.Where(k => filter.Length == 0 || k.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0));
            _items.ClearOptions();
            _items.AddOptions(Shown.Select(s => s.Name).ToList());
            int keep = Shown.FindIndex(s => s.Prefab == selected);
            _items.value = Math.Max(0, keep);
            _items.RefreshShownValue();
        }

        private static string Selected() => _items != null && _items.value >= 0 && _items.value < Shown.Count ? Shown[_items.value].Prefab : null;

        private static void Refresh()
        {
            if (_chest == null) return;
            IReadOnlyList<DeliveryRequest> lines = _chest.Ledger.Lines;
            for (int i = 0; i < RowTexts.Count; i++)
            {
                bool used = i < lines.Count;
                RowTexts[i].text = used ? LineText(lines[i]) : "";
                RowButtons[2 * i].SetActive(used);
                RowButtons[2 * i + 1].SetActive(used);
            }
            _hint.text = _chest.ReadOnly ? Localization.instance.Localize("$haugbui_delivery_readonly") : "";
        }

        private static string LineText(DeliveryRequest line)
        {
            string name = Known.FirstOrDefault(k => k.Prefab == line.ItemPrefab).Name ?? ItemName(line.ItemPrefab);
            string state = DeliveryLedger.IsComplete(line)
                ? "<color=#7fd27f>" + Localization.instance.Localize("$haugbui_delivery_done") + "</color>"
                : Localization.instance.Localize("$haugbui_delivery_remaining", line.Remaining.ToString());
            return $"{name}  {line.Delivered}/{line.Requested}  {state}";
        }

        private static string ItemName(string prefab)
        {
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
        }

        private static void EditLine(int index)
        {
            IReadOnlyList<DeliveryRequest> lines = _chest?.Ledger.Lines;
            if (lines == null || index >= lines.Count) return;
            _filter.text = "";
            ApplyFilter();
            int option = Shown.FindIndex(s => s.Prefab == lines[index].ItemPrefab);
            if (option >= 0)
            {
                _items.value = option;
                _items.RefreshShownValue();
            }
            _amount.text = lines[index].Requested.ToString();
        }

        private static void CancelLine(int index)
        {
            _chest?.Edit(ledger => ledger.Cancel(index) == RequestEditResult.Ok);
            Refresh();
        }

        private static void OnClearCompleted()
        {
            _chest?.Edit(ledger => ledger.ClearCompleted() > 0);
            Refresh();
        }

        private static void OnRequest()
        {
            string prefab = Selected();
            int.TryParse(_amount.text, out int amount);
            RequestEditResult? result = Submit(prefab, amount);
            string error = result == null ? "$haugbui_delivery_err_save" : Error(result.Value);
            if (error != null) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localization.instance.Localize(error));
        }

        private static string Error(RequestEditResult result)
        {
            switch (result)
            {
                case RequestEditResult.Ok: return null;
                case RequestEditResult.InvalidItem: return "$haugbui_delivery_err_item";
                case RequestEditResult.InvalidAmount: return "$haugbui_delivery_err_amount";
                case RequestEditResult.TooManyLines: return "$haugbui_delivery_err_full";
                default: return "$haugbui_delivery_err_save";
            }
        }
    }
}
