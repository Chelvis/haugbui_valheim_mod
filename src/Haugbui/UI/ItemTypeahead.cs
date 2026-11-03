using System;
using System.Collections.Generic;
using Haugbui.Creature;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Haugbui.UI
{
    /// <summary>
    /// One search field. Typing filters the suggestions under it. A click adds the item, hides the
    /// suggestions and leaves the typed text. Suggestions come back only on the next focus.
    /// </summary>
    internal sealed class ItemTypeahead
    {
        private const float RowHeight = 28f;
        private const float MaxHeight = 160f;

        private readonly InputField _field;
        private readonly PanelChrome.ScrollView _popup;
        private readonly List<(string Prefab, string Name)> _known = new List<(string Prefab, string Name)>();
        private readonly List<(string Prefab, string Name)> _shown = new List<(string Prefab, string Name)>();
        private bool _collapsed;
        private string _builtFor;

        public ItemTypeahead(GUIManager gui, Transform parent, float left, float top, float width, Action<string> picked)
        {
            Picked = picked;
            float center = left + width / 2f;
            _field = gui.CreateInputField(parent, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(center, -(top + 16f)),
                InputField.ContentType.Standard, Localization.instance.Localize("$haugbui_filter_search"), 16, width, 32f).GetComponent<InputField>();
            _field.onValueChanged.AddListener(_ => _builtFor = null);
            _popup = PanelChrome.Scroll(gui, parent, left, top + 36f, width, MaxHeight, new Color(0.16f, 0.16f, 0.16f, 0.96f));
            _popup.Root.SetActive(false);
        }

        public Action<string> Picked { get; }
        public InputField Field => _field;

        /// <summary>Transportable items the player has discovered. Equipment never appears.</summary>
        public static List<(string Prefab, string Name)> LoadKnown()
        {
            var known = new List<(string Prefab, string Name)>();
            Player player = Player.m_localPlayer;
            if (ObjectDB.instance == null || player == null) return known;
            foreach (GameObject prefab in ObjectDB.instance.m_items)
            {
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null || drop.m_itemData == null || !ItemBridge.IsTransportable(drop.m_itemData)) continue;
                string shared = drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
                if (string.IsNullOrEmpty(shared) || !player.IsMaterialKnown(shared)) continue;
                known.Add((prefab.name, Localization.instance.Localize(shared)));
            }
            known.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return known;
        }
        public bool SuggestionsVisible => _popup.Root.activeSelf;
        public string Text => _field.text ?? "";
        public IReadOnlyList<(string Prefab, string Name)> Known => _known;

        public void SetKnown(IEnumerable<(string Prefab, string Name)> items)
        {
            _known.Clear();
            if (items != null) _known.AddRange(items);
            _builtFor = null;
        }

        public void SetText(string text)
        {
            _field.text = text ?? "";
        }

        public void Focus()
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_field.gameObject);
            _field.ActivateInputField();
        }

        public void Blur()
        {
            _collapsed = false;
            _field.DeactivateInputField();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _field.gameObject)
                EventSystem.current.SetSelectedGameObject(null);
            Hide();
        }

        /// <summary>Same path as typing into the field and focusing it.</summary>
        public void Type(string text)
        {
            _collapsed = false;
            SetText(text);
            Focus();
            Show();
        }

        public Button Suggestion(string prefab)
        {
            Transform found = _popup.Content.Find("suggest:" + prefab);
            return found != null ? found.GetComponent<Button>() : null;
        }

        public void Tick()
        {
            if (!Focused)
            {
                _collapsed = false;
                Hide();
                return;
            }
            if (_collapsed) Hide();
            else Show();
        }

        /// <summary>The list sits on top of the suggestions. While they are open it must not take the click.</summary>
        public void Cover(GameObject listRoot)
        {
            if (listRoot == null) return;
            bool block = !SuggestionsVisible;
            Raycast(listRoot, block);
            Transform view = listRoot.transform.Find("viewport");
            if (view != null) Raycast(view.gameObject, block);
        }

        public void RaiseAbove(Transform front)
        {
            if (_popup.Root.activeSelf) _popup.Root.transform.SetAsLastSibling();
            if (front != null) front.SetAsLastSibling();
        }

        private bool Focused =>
            _field.isFocused || (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _field.gameObject);

        private void Show()
        {
            string text = Text;
            if (_builtFor != text) Rebuild(text);
            if (_shown.Count == 0)
            {
                Hide();
                return;
            }
            float height = Mathf.Min(MaxHeight, _shown.Count * RowHeight + 8f);
            _popup.Root.GetComponent<RectTransform>().sizeDelta = new Vector2(_popup.Root.GetComponent<RectTransform>().sizeDelta.x, height);
            if (!_popup.Root.activeSelf) _popup.Root.SetActive(true);
            _popup.Root.transform.SetAsLastSibling();
        }

        private void Hide()
        {
            if (_popup.Root.activeSelf) _popup.Root.SetActive(false);
        }

        private void Rebuild(string text)
        {
            _builtFor = text;
            _shown.Clear();
            string filter = text.Trim();
            foreach (var item in _known)
                if (filter.Length == 0 || item.Name.IndexOf(filter, StringComparison.CurrentCultureIgnoreCase) >= 0)
                    _shown.Add(item);

            GUIManager gui = GUIManager.Instance;
            PanelChrome.Clear(_popup.Content);
            foreach (var item in _shown)
            {
                string prefab = item.Prefab;
                GameObject button = gui.CreateButton(item.Name, _popup.Content, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero,
                    100f, RowHeight - 2f);
                button.name = "suggest:" + prefab;
                Button click = button.GetComponent<Button>();
                // A focused field consumes the click, so onClick never runs. Pointer down still arrives.
                button.AddComponent<SuggestionPress>().Bind(Pick, prefab);
                click.onClick.AddListener(() => Pick(prefab));
            }
            PanelChrome.Stack(_popup.Content, RowHeight);
        }

        private static void Raycast(GameObject go, bool enabled)
        {
            Graphic graphic = go.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = enabled;
        }

        private void Pick(string prefab)
        {
            if (_collapsed) return;
            _collapsed = true;
            Hide();
            Picked?.Invoke(prefab);
        }
    }

    internal sealed class SuggestionPress : MonoBehaviour, IPointerDownHandler
    {
        private Action<string> _pick;
        private string _prefab;

        public void Bind(Action<string> pick, string prefab)
        {
            _pick = pick;
            _prefab = prefab;
        }

        public void OnPointerDown(PointerEventData eventData) => _pick?.Invoke(_prefab);
    }
}
