using System;
using System.Collections.Generic;
using Haugbui.Core.Persistence;
using Haugbui.Core.Storage;
using Haugbui.Integration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>
    /// Accept-only list of one vanilla chest. An empty list keeps the existing-item rule.
    /// Each row can cap how many stacks of that item the chest may hold. An empty field is no cap.
    /// Changes save immediately. Stacks already over the cap stay in the chest.
    /// </summary>
    internal static class ChestFilterPanel
    {
        private const float Width = 720f;
        private const float Height = 540f;
        private const float RowHeight = 34f;
        private const float Inset = 24f;
        private const string Unlimited = "\u221E";

        private static GameObject _root;
        private static GameObject _headers;
        private static ItemTypeahead _search;
        private static PanelChrome.ScrollView _list;
        private static Button _clear;
        private static Button _copy;
        private static Button _paste;
        private static Button _dismiss;
        private static Container _chest;
        private static float _openedAt;
        private static List<ChestFilterLine> _filter = new List<ChestFilterLine>();
        private static bool _rebuilding;
        private static bool _rebuildAgain;

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static Container Current => IsOpen ? _chest : null;
        internal static Button Dismiss => _dismiss;
        internal static Button ClearButton => _clear;
        internal static Button CopyButton => _copy;
        internal static Button PasteButton => _paste;
        internal static IReadOnlyList<string> Lines => _filter.ConvertAll(line => line.Prefab);
        internal static bool SuggestionsVisible => _search != null && _search.SuggestionsVisible;
        internal static string SearchText => _search != null ? _search.Text : "";
        internal static IReadOnlyList<(string Prefab, string Name)> Known => _search != null ? _search.Known : Array.Empty<(string, string)>();

        public static void Open(Container chest)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || chest == null) return;
            if (_root == null) Build();
            _chest = chest;
            _openedAt = Time.time;
            _search.SetKnown(ItemTypeahead.LoadKnown());
            _search.SetText("");
            Read();
            Rebuild();
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
        }

        public static void Update()
        {
            if (!IsOpen) return;
            if (_chest == null || Player.m_localPlayer == null || PanelInput.ShouldClose(_openedAt))
            {
                Close();
                return;
            }
            _search.Tick();
            _search.RaiseAbove(_dismiss.transform);
            _search.Cover(_list.Root);
        }

        internal static void TypeSearch(string text) => _search?.Type(text);
        internal static void BlurSearch() => _search?.Blur();
        internal static Button Suggestion(string prefab) => _search?.Suggestion(prefab);

        internal static InputField StacksField(string prefab)
        {
            if (_list.Content == null || string.IsNullOrEmpty(prefab)) return null;
            Transform row = null;
            for (int i = 0; i < _list.Content.childCount; i++)
            {
                if (_list.Content.GetChild(i).name != "row:" + prefab) continue;
                row = _list.Content.GetChild(i);
                break;
            }
            return row != null ? row.Find("stacks:" + prefab)?.GetComponent<InputField>() : null;
        }

        internal static Button RemoveButton(string prefab)
        {
            if (_list.Content == null || string.IsNullOrEmpty(prefab)) return null;
            string rowName = "row:" + prefab;
            string buttonName = "remove:" + prefab;
            for (int i = 0; i < _list.Content.childCount; i++)
            {
                Transform child = _list.Content.GetChild(i);
                if (child.name == buttonName) return ButtonOf(child);
                if (child.name != rowName) continue;
                Transform named = child.Find(buttonName);
                Button button = named != null ? ButtonOf(named) : child.GetComponentInChildren<Button>(true);
                if (button != null) return button;
            }
            return null;
        }

        /// <summary>Row and child names, for a failed self-test check.</summary>
        internal static string DescribeRows()
        {
            if (_list.Content == null) return "no content";
            var parts = new List<string>();
            for (int i = 0; i < _list.Content.childCount; i++)
            {
                Transform row = _list.Content.GetChild(i);
                var children = new List<string>();
                for (int j = 0; j < row.childCount; j++)
                {
                    Transform child = row.GetChild(j);
                    children.Add(child.GetComponent<Button>() != null ? child.name + ":button" : child.name);
                }
                parts.Add(row.name + "[" + string.Join(" ", children) + "]");
            }
            string rows = parts.Count == 0 ? "empty" : string.Join(" ", parts);
            return "lines=" + string.Join(",", _filter.ConvertAll(line => line.Prefab)) + " " + rows;
        }

        private static Button ButtonOf(Transform node) =>
            node.GetComponent<Button>() ?? node.GetComponentInChildren<Button>(true);

        /// <summary>Same path as Clear all.</summary>
        internal static void Clear()
        {
            if (_chest == null) return;
            _filter = new List<ChestFilterLine>();
            Save();
            Rebuild();
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            PanelChrome.TopLeft(gui, _root.transform, Localization.instance.Localize("$haugbui_filter_title"),
                Inset, 20f, Width - Inset - 72f, 36f, 24);

            _search = new ItemTypeahead(gui, _root.transform, Inset, 64f, Width - Inset * 2f, Add);
            _headers = Header(gui);
            _list = PanelChrome.Scroll(gui, _root.transform, Inset, 148f, Width - Inset * 2f, Height - 148f - 110f, new Color(1f, 1f, 1f, 0f));
            gui.CreateText(Localization.instance.Localize("$haugbui_filter_keep_extra"), _root.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 78f), gui.AveriaSerifBold, 14, new Color(0.9f, 0.75f, 0.4f), true, Color.black, Width - 48f, 24f, false);

            _clear = Bottom(gui, "$haugbui_filter_clear", -226f, Clear);
            _copy = Bottom(gui, "$haugbui_filter_copy", 0f, Copy);
            _paste = Bottom(gui, "$haugbui_filter_paste", 226f, Paste);
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
            Column(gui, row.transform, "$haugbui_filter_col_item", NameLeft);
            Column(gui, row.transform, "$haugbui_filter_col_stacks", StacksLeft);
            return row;
        }

        private const float NameLeft = 10f;
        private const float StacksLeft = 295f;
        private const float StacksWidth = 90f;

        private static void Column(GUIManager gui, Transform parent, string token, float left) =>
            PanelChrome.LeftLabel(gui, parent, Localization.instance.Localize(token), left + PanelChrome.ScrollInset, 160f, 15, gui.ValheimOrange);

        private static Button Bottom(GUIManager gui, string token, float x, UnityEngine.Events.UnityAction action)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(token), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, 32f), 210f, 36f);
            Button component = button.GetComponent<Button>();
            component.onClick.AddListener(action);
            return component;
        }

        private static void Read()
        {
            ZNetView view = _chest != null ? _chest.m_nview : null;
            _filter = ChestFilterStore.Read(view != null ? view.GetZDO() : null);
        }

        // A failed write reloads the saved list. Rebuilding from here runs inside the click and drops the new row.
        private static bool Save()
        {
            ZNetView view = _chest != null ? _chest.m_nview : null;
            if (view == null || !view.IsValid()) return false;
            if (!view.IsOwner()) view.ClaimOwnership();
            List<ChestFilterLine> next = ChestFilterRules.Normalize(_filter);
            if (ChestFilterStore.Write(view.GetZDO(), next))
            {
                _filter = next;
                return true;
            }
            _filter = ChestFilterStore.Read(view.GetZDO());
            return false;
        }

        private static void Add(string prefab)
        {
            if (_chest == null || string.IsNullOrEmpty(prefab)) return;
            if (!_filter.Exists(line => string.Equals(line.Prefab, prefab, StringComparison.Ordinal)))
            {
                if (_filter.Count >= ChestFilterRules.MaxEntries)
                {
                    Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$haugbui_filter_full");
                    return;
                }
                _filter.Add(new ChestFilterLine(prefab, 0));
                if (!Save()) return;
            }
            AppendRow(prefab);
        }

        private static void Remove(string prefab)
        {
            if (_chest == null) return;
            if (_filter.RemoveAll(line => string.Equals(line.Prefab, prefab, StringComparison.Ordinal)) == 0) return;
            Save();
            Rebuild();
        }

        private static void OnStacks(string prefab, string text)
        {
            int index = _filter.FindIndex(line => string.Equals(line.Prefab, prefab, StringComparison.Ordinal));
            if (index < 0 || !TryStacks(text, out int stacks)) return;
            if (stacks != _filter[index].MaxStacks)
            {
                _filter[index] = new ChestFilterLine(prefab, stacks);
                Save();
            }
            InputField field = StacksField(prefab);
            string shown = stacks > 0 ? stacks.ToString() : "";
            if (field != null && field.text != shown) field.SetTextWithoutNotify(shown);
        }

        private static bool TryStacks(string text, out int stacks)
        {
            stacks = 0;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!int.TryParse(text, out int parsed)) return false;
            if (parsed < 1) return true;
            stacks = Math.Min(parsed, ChestFilterRules.MaxStackLimit);
            return true;
        }

        private static void Rebuild()
        {
            if (_list.Content == null) return;
            // A field callback during DestroyImmediate would rebuild from a half-updated list and wipe the new rows.
            if (_rebuilding)
            {
                _rebuildAgain = true;
                return;
            }
            _rebuilding = true;
            try
            {
                int guard = 0;
                do
                {
                    _rebuildAgain = false;
                    BuildRows();
                    AddMissingRows();
                }
                while (_rebuildAgain && ++guard < 4);
                RefreshClipboardButtons();
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private static void BuildRows()
        {
            if (_headers != null) _headers.SetActive(_filter.Count > 0);
            GUIManager gui = GUIManager.Instance;
            ChestFilterLine[] lines = _filter.ToArray();
            ClearRows();
            var fields = new List<(InputField Field, string Prefab)>();
            foreach (ChestFilterLine line in lines)
                if (line != null) fields.Add(CreateRow(gui, line));
            PanelChrome.Stack(_list.Content, RowHeight);
            WireStackFields(fields);
        }

        /// <summary>Adds the one row for a line already stored. Does not touch the other rows.</summary>
        private static void AppendRow(string prefab)
        {
            if (_list.Content == null || string.IsNullOrEmpty(prefab) || RemoveButton(prefab) != null) return;
            ChestFilterLine line = _filter.Find(item => string.Equals(item.Prefab, prefab, StringComparison.Ordinal));
            if (line == null) return;
            if (_headers != null) _headers.SetActive(true);
            (InputField field, string name) created = CreateRow(GUIManager.Instance, line);
            PanelChrome.Stack(_list.Content, RowHeight);
            WireStackFields(new List<(InputField Field, string Prefab)> { created });
        }

        /// <summary>A callback mid-build can leave a saved line with no row. Add the missing ones from the final list.</summary>
        private static void AddMissingRows()
        {
            var present = new HashSet<string>();
            for (int i = 0; i < _list.Content.childCount; i++)
            {
                string name = _list.Content.GetChild(i).name;
                if (name.StartsWith("row:", StringComparison.Ordinal)) present.Add(name.Substring(4));
            }
            GUIManager gui = GUIManager.Instance;
            var fields = new List<(InputField Field, string Prefab)>();
            bool added = false;
            foreach (ChestFilterLine line in _filter)
            {
                if (line == null || present.Contains(line.Prefab)) continue;
                fields.Add(CreateRow(gui, line));
                added = true;
            }
            if (!added) return;
            PanelChrome.Stack(_list.Content, RowHeight);
            WireStackFields(fields);
        }

        private static (InputField Field, string Prefab) CreateRow(GUIManager gui, ChestFilterLine line)
        {
            var row = new GameObject("row:" + line.Prefab, typeof(RectTransform));
            row.transform.SetParent(_list.Content, false);
            PanelChrome.LeftLabel(gui, row.transform, Display(line.Prefab), NameLeft, StacksLeft - NameLeft - 16f, 16, Color.white, HorizontalWrapMode.Wrap);
            InputField stacks = gui.CreateInputField(row.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(StacksLeft + StacksWidth / 2f, 0f),
                InputField.ContentType.IntegerNumber, Unlimited, 16, StacksWidth, 28f).GetComponent<InputField>();
            stacks.gameObject.name = "stacks:" + line.Prefab;
            stacks.text = line.MaxStacks > 0 ? line.MaxStacks.ToString() : "";
            string captured = line.Prefab;
            GameObject remove = gui.CreateButton(Localization.instance.Localize("$haugbui_panel_dismiss"), row.transform,
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 0f), 32f, 28f);
            remove.name = "remove:" + line.Prefab;
            remove.GetComponent<Button>().onClick.AddListener(() => Remove(captured));
            return (stacks, line.Prefab);
        }

        private static void WireStackFields(List<(InputField Field, string Prefab)> fields)
        {
            foreach ((InputField field, string prefab) in fields)
            {
                string captured = prefab;
                field.onValueChanged.AddListener(value => OnStacks(captured, value));
            }
        }

        /// <summary>Drop field listeners before DestroyImmediate, so a dying field cannot rebuild the list.</summary>
        private static void Copy()
        {
            if (_chest == null) return;
            PanelClipboard.CopyFilters(_filter);
            RefreshClipboardButtons();
        }

        /// <summary>Replaces the open chest's whole list, including stack caps, and saves it.</summary>
        private static void Paste()
        {
            if (_chest == null || !PanelClipboard.HasFilters) return;
            _filter = PanelClipboard.FilterLines();
            Save();
            Rebuild();
        }

        private static void RefreshClipboardButtons()
        {
            if (_copy != null) _copy.interactable = _chest != null;
            if (_paste != null) _paste.interactable = _chest != null && PanelClipboard.HasFilters;
        }

        private static void ClearRows()
        {
            for (int i = _list.Content.childCount - 1; i >= 0; i--)
            {
                Transform row = _list.Content.GetChild(i);
                InputField field = row.GetComponentInChildren<InputField>(true);
                if (field != null) field.onValueChanged.RemoveAllListeners();
                UnityEngine.Object.DestroyImmediate(row.gameObject);
            }
        }

        private static string Display(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
            string token = drop != null ? drop.m_itemData.m_shared.m_name : prefab;
            return Localization.instance.Localize(token);
        }
    }
}
