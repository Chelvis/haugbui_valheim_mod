using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Structures;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>
    /// Request lines of one Delivery Chest. The list is a draft until Save, which writes the same ledger.
    /// Closing without Save keeps what was already stored. A new row's amount starts empty.
    /// </summary>
    internal static class DeliveryPanel
    {
        private const float Width = 860f;
        private const float Height = 540f;
        private const float RowHeight = 36f;
        private const float Inset = 24f;

        private sealed class DraftLine
        {
            public DraftLine(string prefab, string amount)
            {
                Prefab = prefab;
                Amount = amount ?? "";
            }

            public string Prefab;
            public string Amount;
        }

        private static GameObject _root;
        private static ItemTypeahead _search;
        private static PanelChrome.ScrollView _list;
        private static Text _empty;
        private static Text _footnote;
        private static GameObject _headers;
        private static Button _clear;
        private static Button _copy;
        private static Button _paste;
        private static Button _save;
        private static Button _dismiss;
        private static DeliveryChest _chest;
        private static float _openedAt;
        private static readonly List<DraftLine> Draft = new List<DraftLine>();
        private static string _built;

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static DeliveryChest Current => IsOpen ? _chest : null;
        internal static Button Dismiss => _dismiss;
        internal static Button ClearButton => _clear;
        internal static Button CopyButton => _copy;
        internal static Button PasteButton => _paste;
        internal static Button SaveButton => _save;
        internal static int RowCount => Draft.Count;
        internal static bool ShowsEmpty => _empty != null && _empty.gameObject.activeSelf;
        internal static string EmptyText => _empty != null ? _empty.text : "";
        internal static string Footnote => _footnote != null ? _footnote.text : "";
        internal static bool SuggestionsVisible => _search != null && _search.SuggestionsVisible;
        internal static string SearchText => _search != null ? _search.Text : "";
        internal static IReadOnlyList<(string Prefab, string Name)> KnownItems => _search != null ? _search.Known : Array.Empty<(string, string)>();

        internal static bool CanSave =>
            _chest != null && !_chest.ReadOnly && Draft.TrueForAll(line => !string.IsNullOrWhiteSpace(line.Amount));

        public static void Open(DeliveryChest chest)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || chest == null) return;
            if (_root == null) Build();
            _chest = chest;
            _openedAt = Time.time;
            _search.SetKnown(ItemTypeahead.LoadKnown());
            _search.SetText("");
            _search.Field.interactable = !chest.ReadOnly;
            LoadDraft();
            Rebuild(force: true);
            if (!_root.activeSelf)
            {
                _root.SetActive(true);
                PanelInput.Acquired();
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            PanelInput.ClearSelection();
            PanelInput.Released();
            _root.SetActive(false);
            _chest = null;
            _built = null;
        }

        public static void Update()
        {
            if (!IsOpen) return;
            if (_chest == null || Player.m_localPlayer == null || PanelInput.ShouldClose(_openedAt))
            {
                Close();
                return;
            }
            if (PanelInput.CopyPressed()) CopyDraft();
            else if (PanelInput.PastePressed()) PasteDraft();
            _search.Tick();
            _search.RaiseAbove(_dismiss.transform);
            _search.Cover(_list.Root);
            if (Time.frameCount % 15 == 0) RefreshCounts();
        }

        internal static void TypeSearch(string text) => _search?.Type(text);
        internal static void BlurSearch() => _search?.Blur();
        internal static Button Suggestion(string prefab) => _search?.Suggestion(prefab);

        internal static void SetAmount(string prefab, string amount)
        {
            DraftLine line = Draft.Find(item => item.Prefab == prefab);
            if (line == null) return;
            line.Amount = amount ?? "";
            InputField field = AmountField(prefab);
            if (field != null && field.text != line.Amount) field.text = line.Amount;
            RefreshButtons();
        }

        internal static int ShownAmount(string prefab)
        {
            Transform row = _list.Content != null ? FindRow(prefab) : null;
            Text text = row != null ? row.Find("count:" + prefab)?.GetComponent<Text>() : null;
            return text != null && int.TryParse(text.text, out int count) ? count : -1;
        }

        internal static Button RemoveButton(string prefab)
        {
            Transform row = FindRow(prefab);
            return row != null ? row.Find("remove:" + prefab)?.GetComponent<Button>() : null;
        }

        /// <summary>Same path as filling the row and pressing Save.</summary>
        internal static RequestEditResult? Submit(string prefab, int amount)
        {
            if (_chest == null) return null;
            if (string.IsNullOrEmpty(prefab)) return RequestEditResult.InvalidItem;
            if (amount <= 0 || amount > DeliveryLedger.MaxRequested) return RequestEditResult.InvalidAmount;
            DraftLine line = Draft.Find(item => item.Prefab == prefab);
            if (line == null)
            {
                if (Draft.Count >= DeliveryLedger.MaxLines) return RequestEditResult.TooManyLines;
                line = new DraftLine(prefab, amount.ToString());
                Draft.Add(line);
            }
            else line.Amount = amount.ToString();
            RequestEditResult result = Write();
            if (result == RequestEditResult.Ok) LoadDraft();
            Rebuild(force: true);
            return result;
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            PanelChrome.TopLeft(gui, _root.transform, Localization.instance.Localize("$haugbui_piece_delivery"),
                Inset, 20f, Width - Inset - 72f, 36f, 26);

            _search = new ItemTypeahead(gui, _root.transform, Inset, 64f, Width - Inset * 2f, Add);
            _headers = Header(gui);
            _list = PanelChrome.Scroll(gui, _root.transform, Inset, 148f, Width - Inset * 2f, Height - 148f - 108f, new Color(1f, 1f, 1f, 0f));
            _empty = gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -250f),
                gui.AveriaSerifBold, 18, Color.white, true, Color.black, Width - 80f, 32f, false).GetComponent<Text>();
            _footnote = gui.CreateText(Localization.instance.Localize("$haugbui_delivery_keep_extra"), _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 78f), gui.AveriaSerifBold, 14, new Color(0.9f, 0.75f, 0.4f), true, Color.black, Width - 48f, 24f, false).GetComponent<Text>();

            _clear = Bottom(gui, "$haugbui_filter_clear", -312f, ClearDraft);
            _copy = Bottom(gui, "$haugbui_delivery_copy", -104f, CopyDraft);
            _paste = Bottom(gui, "$haugbui_delivery_paste", 104f, PasteDraft);
            _save = Bottom(gui, "$haugbui_job_save", 312f, OnSave);
            _dismiss = PanelChrome.CornerDismiss(gui, _root.transform, Close);
            _root.SetActive(false);
        }

        private static GameObject Header(GUIManager gui)
        {
            var row = new GameObject("headers", typeof(RectTransform));
            row.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)row.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(Inset, -116f);
            rect.sizeDelta = new Vector2(Width - Inset * 2f, 28f);
            Column(gui, row.transform, "$haugbui_delivery_col_item", ItemLeft);
            Column(gui, row.transform, "$haugbui_delivery_amount", AmountLeft);
            Column(gui, row.transform, "$haugbui_delivery_col_requested", RequestedLeft);
            return row;
        }

        private const float ItemLeft = 15f;
        private const float AmountLeft = 275f;
        private const float RequestedLeft = 395f;
        private const float RequestedWidth = 110f;

        private static void Column(GUIManager gui, Transform parent, string token, float left) =>
            PanelChrome.LeftLabel(gui, parent, Localization.instance.Localize(token), left + PanelChrome.ScrollInset, 160f, 15, gui.ValheimOrange);

        private static Button Bottom(GUIManager gui, string token, float x, Action action)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(token), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 36f), 190f, 36f);
            Button component = button.GetComponent<Button>();
            component.onClick.AddListener(() => action());
            return component;
        }

        private static void LoadDraft()
        {
            Draft.Clear();
            if (_chest == null) return;
            foreach (IGrouping<string, DeliveryRequest> group in _chest.Ledger.Lines.GroupBy(line => line.ItemPrefab))
            {
                DeliveryRequest best = group.OrderByDescending(line => line.Delivered).First();
                Draft.Add(new DraftLine(best.ItemPrefab, best.Requested > 0 ? best.Requested.ToString() : ""));
            }
        }

        private static void Add(string prefab)
        {
            if (_chest == null || _chest.ReadOnly || string.IsNullOrEmpty(prefab)) return;
            if (Draft.Exists(line => line.Prefab == prefab)) return;
            if (Draft.Count >= DeliveryLedger.MaxLines)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$haugbui_delivery_err_full"));
                return;
            }
            Draft.Add(new DraftLine(prefab, ""));
            Rebuild(force: true);
        }

        private static void Remove(string prefab)
        {
            if (_chest == null || _chest.ReadOnly) return;
            if (Draft.RemoveAll(line => line.Prefab == prefab) == 0) return;
            Rebuild(force: true);
        }

        private static void ClearDraft()
        {
            if (_chest == null || _chest.ReadOnly || Draft.Count == 0) return;
            Draft.Clear();
            Rebuild(force: true);
        }

        private static bool CanCopy =>
            _chest != null && Draft.TrueForAll(line =>
                int.TryParse(line.Amount, out int amount) && amount > 0 && amount <= DeliveryLedger.MaxRequested);

        private static void CopyDraft()
        {
            if (!CanCopy) return;
            var lines = new List<(string Prefab, int Requested)>();
            foreach (DraftLine line in Draft)
            {
                if (!int.TryParse(line.Amount, out int amount)) return;
                lines.Add((line.Prefab, amount));
            }
            PanelClipboard.CopyRequests(lines);
            RefreshButtons();
        }

        /// <summary>Replaces the open chest's whole request list and writes it. Already delivered amounts stay.</summary>
        private static void PasteDraft()
        {
            if (_chest == null || _chest.ReadOnly || !PanelClipboard.HasRequests) return;
            Draft.Clear();
            foreach ((string prefab, int requested) in PanelClipboard.Requests)
                Draft.Add(new DraftLine(prefab, requested.ToString()));
            RequestEditResult result = Write();
            if (result != RequestEditResult.Ok)
            {
                string error = Error(result);
                if (error != null) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localization.instance.Localize(error));
            }
            LoadDraft();
            Rebuild(force: true);
        }

        private static void OnSave()
        {
            if (!CanSave) return;
            RequestEditResult result = Write();
            if (result == RequestEditResult.Ok)
            {
                LoadDraft();
                Close();
                return;
            }
            string error = Error(result);
            if (error != null) Player.m_localPlayer?.Message(MessageHud.MessageType.Center, Localization.instance.Localize(error));
            Rebuild(force: true);
        }

        private static RequestEditResult Write()
        {
            if (_chest == null) return RequestEditResult.InvalidItem;
            var lines = new List<(string Prefab, int Requested)>();
            foreach (DraftLine line in Draft)
            {
                if (!int.TryParse(line.Amount, out int amount)) return RequestEditResult.InvalidAmount;
                lines.Add((line.Prefab, amount));
            }
            RequestEditResult result = RequestEditResult.Ok;
            bool saved = _chest.Edit(ledger => (result = ledger.Replace(lines)) == RequestEditResult.Ok);
            if (!saved && result == RequestEditResult.Ok) return RequestEditResult.InvalidItem;
            return result;
        }

        private static void Rebuild(bool force)
        {
            string signature = string.Join("|", Draft.Select(line => line.Prefab));
            bool same = !force && signature == _built && _list.Content != null && _list.Content.childCount == Draft.Count;
            _built = signature;
            bool empty = Draft.Count == 0;
            if (_empty != null)
            {
                _empty.gameObject.SetActive(empty);
                _empty.text = empty
                    ? (_chest != null && _chest.ReadOnly
                        ? Localization.instance.Localize("$haugbui_delivery_readonly")
                        : Localization.instance.Localize("$haugbui_delivery_none"))
                    : "";
            }
            if (_headers != null) _headers.SetActive(!empty);
            if (_footnote != null && _chest != null && _chest.ReadOnly && !empty)
                _footnote.text = Localization.instance.Localize("$haugbui_delivery_readonly");
            else if (_footnote != null)
                _footnote.text = Localization.instance.Localize("$haugbui_delivery_keep_extra");

            if (!same)
            {
                GUIManager gui = GUIManager.Instance;
                PanelChrome.Clear(_list.Content);
                foreach (DraftLine line in Draft)
                {
                    var row = new GameObject("row:" + line.Prefab, typeof(RectTransform));
                    row.transform.SetParent(_list.Content, false);
                    PanelChrome.LeftLabel(gui, row.transform, ItemName(line.Prefab), ItemLeft, AmountLeft - ItemLeft - 16f, 16, Color.white, HorizontalWrapMode.Wrap);
                    Text count = PanelChrome.LeftLabel(gui, row.transform, "0", AmountLeft, 80f, 16, Color.white);
                    count.gameObject.name = "count:" + line.Prefab;
                    InputField amount = gui.CreateInputField(row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(RequestedLeft + RequestedWidth / 2f, 0f),
                        InputField.ContentType.IntegerNumber, Localization.instance.Localize("$haugbui_delivery_amount"), 16, RequestedWidth, 28f).GetComponent<InputField>();
                    amount.text = line.Amount;
                    amount.interactable = _chest == null || !_chest.ReadOnly;
                    DraftLine captured = line;
                    amount.onValueChanged.AddListener(value =>
                    {
                        captured.Amount = value;
                        PaintCount(count, captured.Prefab, value);
                        RefreshButtons();
                    });
                    if (_chest == null || !_chest.ReadOnly)
                    {
                        GameObject remove = gui.CreateButton(Localization.instance.Localize("$haugbui_panel_dismiss"), row.transform,
                            new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 0f), 32f, 28f);
                        remove.name = "remove:" + line.Prefab;
                        string prefab = line.Prefab;
                        remove.GetComponent<Button>().onClick.AddListener(() => Remove(prefab));
                    }
                }
                PanelChrome.Stack(_list.Content, RowHeight);
            }
            RefreshCounts();
            RefreshButtons();
        }

        private static void RefreshCounts()
        {
            if (_list.Content == null || _chest == null) return;
            foreach (DraftLine line in Draft)
            {
                Transform row = FindRow(line.Prefab);
                Text text = row != null ? row.Find("count:" + line.Prefab)?.GetComponent<Text>() : null;
                if (text != null) PaintCount(text, line.Prefab, line.Amount);
            }
        }

        private static void RefreshButtons()
        {
            if (_clear != null) _clear.interactable = _chest != null && !_chest.ReadOnly && Draft.Count > 0;
            if (_copy != null) _copy.interactable = CanCopy;
            if (_paste != null) _paste.interactable = _chest != null && !_chest.ReadOnly && PanelClipboard.HasRequests;
            if (_save != null) _save.interactable = CanSave;
        }

        private static readonly Color ShortColor = new Color(0.95f, 0.78f, 0.2f);
        private static readonly Color MetColor = new Color(0.4f, 0.85f, 0.4f);

        /// <summary>Yellow while the chest holds less than requested; green at or above. An empty or invalid amount stays white.</summary>
        private static void PaintCount(Text text, string prefab, string requested)
        {
            int have = InChest(prefab);
            text.text = have.ToString();
            text.color = int.TryParse(requested, out int want) && want > 0
                ? (have < want ? ShortColor : MetColor)
                : Color.white;
        }

        private static int InChest(string prefab)
        {
            Container container = _chest != null ? _chest.Container : null;
            if (container == null) return 0;
            int count = 0;
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems())
                if (item.m_dropPrefab != null && item.m_dropPrefab.name == prefab)
                    count += item.m_stack;
            return count;
        }

        private static Transform FindRow(string prefab)
        {
            if (_list.Content == null) return null;
            foreach (Transform row in _list.Content)
                if (row.name == "row:" + prefab) return row;
            return null;
        }

        private static InputField AmountField(string prefab)
        {
            Transform row = FindRow(prefab);
            return row != null ? row.GetComponentInChildren<InputField>() : null;
        }

        private static string ItemName(string prefab)
        {
            (string Prefab, string Name) known = _search != null
                ? _search.Known.FirstOrDefault(item => item.Prefab == prefab)
                : default;
            if (!string.IsNullOrEmpty(known.Name)) return known.Name;
            ItemDrop drop = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>() : null;
            return drop != null ? Localization.instance.Localize(drop.m_itemData.m_shared.m_name) : prefab;
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
