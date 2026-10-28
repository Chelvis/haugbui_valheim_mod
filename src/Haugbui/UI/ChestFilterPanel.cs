using System;
using System.Collections.Generic;
using System.Linq;
using Haugbui.Core.Storage;
using Haugbui.Creature;
using Haugbui.Integration;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>Prefab filter of one vanilla chest. Opened with alt-interact. An empty list keeps HB-062.</summary>
    internal static class ChestFilterPanel
    {
        private const float Width = 460f;
        private const float Height = 420f;

        private static GameObject _root;
        private static Text _list;
        private static InputField _search;
        private static Dropdown _items;
        private static Dropdown _chosen;
        private static Container _chest;
        private static float _openedAt;
        private static readonly List<(string Prefab, string Name)> Known = new List<(string, string)>();
        private static readonly List<(string Prefab, string Name)> Shown = new List<(string, string)>();
        private static List<string> _filter = new List<string>();

        public static bool IsOpen => _root != null && _root.activeSelf;
        internal static Container Current => IsOpen ? _chest : null;

        public static void Open(Container chest)
        {
            if (GUIManager.IsHeadless() || GUIManager.CustomGUIFront == null || chest == null) return;
            if (_root == null) Build();
            _chest = chest;
            _openedAt = Time.time;
            LoadKnown();
            _search.text = "";
            Read();
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
            _root.SetActive(false);
            PanelInput.Released();
            _chest = null;
        }

        public static void Update()
        {
            if (!IsOpen) return;
            if (_chest == null || Player.m_localPlayer == null || PanelInput.ShouldClose(_openedAt)) Close();
        }

        /// <summary>Same path as Clear all.</summary>
        internal static void Clear()
        {
            if (_chest == null) return;
            _filter = new List<string>();
            Save();
            Refresh();
        }

        private static void Build()
        {
            GUIManager gui = GUIManager.Instance;
            _root = gui.CreateWoodpanel(GUIManager.CustomGUIFront.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, Width, Height, draggable: false);
            gui.CreateText(Localization.instance.Localize("$haugbui_filter_title"), _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0, -32f), gui.AveriaSerifBold, 24, gui.ValheimOrange, true, Color.black, Width - 40f, 36f, false);
            _list = gui.CreateText("", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -130f),
                gui.AveriaSerifBold, 16, Color.white, true, Color.black, Width - 50f, 140f, false).GetComponent<Text>();

            _search = gui.CreateInputField(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(110f, -230f),
                InputField.ContentType.Standard, Localization.instance.Localize("$haugbui_filter_search"), 16, 180f, 32f).GetComponent<InputField>();
            _search.onValueChanged.AddListener(_ => ApplySearch());
            _items = gui.CreateDropDown(_root.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330f, -230f), 16, 220f, 32f)
                .GetComponent<Dropdown>();
            _chosen = gui.CreateDropDown(_root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -280f), 16, 300f, 32f)
                .GetComponent<Dropdown>();

            Button(gui, "$haugbui_filter_add", -155f, 78f, AddSelected);
            Button(gui, "$haugbui_filter_remove", 0f, 78f, RemoveSelected);
            Button(gui, "$haugbui_filter_clear", 155f, 78f, Clear);
            Button(gui, "$haugbui_panel_close", 0f, 28f, Close);
            _root.SetActive(false);
        }

        private static void Button(GUIManager gui, string label, float x, float y, Action action)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize(label), _root.transform,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(x, y), 140f, 36f);
            button.GetComponent<Button>().onClick.AddListener(() => action());
        }

        private static void LoadKnown()
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

        private static void Read()
        {
            ZNetView view = _chest != null ? _chest.m_nview : null;
            _filter = ChestFilterStore.Read(view != null ? view.GetZDO() : null);
        }

        private static void Save()
        {
            ZNetView view = _chest != null ? _chest.m_nview : null;
            if (view == null || !view.IsValid()) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            ChestFilterStore.Write(view.GetZDO(), _filter);
            _filter = ChestFilterStore.Read(view.GetZDO());
        }

        private static void ApplySearch()
        {
            string filter = _search != null ? _search.text?.Trim() ?? "" : "";
            string selected = _items != null && _items.value >= 0 && _items.value < Shown.Count ? Shown[_items.value].Prefab : null;
            Shown.Clear();
            Shown.AddRange(Known.Where(k => filter.Length == 0 || k.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0));
            _items.ClearOptions();
            _items.AddOptions(Shown.Select(s => s.Name).ToList());
            int keep = Shown.FindIndex(s => s.Prefab == selected);
            _items.value = Math.Max(0, keep);
            _items.RefreshShownValue();
        }

        private static void Refresh()
        {
            if (_list == null) return;
            ApplySearch();
            if (_filter.Count == 0) _list.text = Localization.instance.Localize("$haugbui_filter_empty");
            else _list.text = Localization.instance.Localize("$haugbui_filter_list", string.Join(", ", _filter.Select(Display)));

            _chosen.ClearOptions();
            _chosen.AddOptions(_filter.Select(Display).ToList());
            _chosen.RefreshShownValue();
        }

        private static string Display(string prefab)
        {
            GameObject item = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(prefab) : null;
            string token = item != null ? item.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : prefab;
            return Localization.instance.Localize(token);
        }

        private static void AddSelected()
        {
            if (_chest == null || Shown.Count == 0 || _items.value < 0 || _items.value >= Shown.Count) return;
            string prefab = Shown[_items.value].Prefab;
            if (_filter.Exists(name => string.Equals(name, prefab, StringComparison.Ordinal))) return;
            if (_filter.Count >= ChestFilterRules.MaxEntries)
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "$haugbui_filter_full");
                return;
            }
            _filter.Add(prefab);
            Save();
            Refresh();
        }

        private static void RemoveSelected()
        {
            if (_chest == null || _filter.Count == 0 || _chosen.value < 0 || _chosen.value >= _filter.Count) return;
            _filter.RemoveAt(_chosen.value);
            Save();
            Refresh();
        }
    }
}
