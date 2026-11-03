using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Jotunn.Managers;

namespace Haugbui.UI
{
    /// <summary>Corner close and a scrolling list, shared by the Haugbui panels.</summary>
    internal static class PanelChrome
    {
        internal readonly struct ScrollView
        {
            public ScrollView(GameObject root, RectTransform content, ScrollRect scroll)
            {
                Root = root;
                Content = content;
                Scroll = scroll;
            }

            public GameObject Root { get; }
            public RectTransform Content { get; }
            public ScrollRect Scroll { get; }
        }

        /// <summary>The scroll viewport is inset by this much. A header outside the list adds it to share a column edge.</summary>
        public const float ScrollInset = 4f;

        private static Sprite _sprite;

        /// <summary>CreateText centers its rect. The pivot is moved so <paramref name="left"/> is the text's left edge.</summary>
        public static Text LeftLabel(GUIManager gui, Transform parent, string text, float left, float width, int fontSize, Color color,
            HorizontalWrapMode overflow = HorizontalWrapMode.Overflow)
        {
            GameObject go = gui.CreateText(text, parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                Vector2.zero, gui.AveriaSerifBold, fontSize, color, true, Color.black, width, 28f, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(left, 0f);
            Text label = go.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = overflow;
            return label;
        }

        /// <summary>Top-left of the text sits <paramref name="top"/> below the parent's top and <paramref name="left"/> from its left.</summary>
        public static Text TopLeft(GUIManager gui, Transform parent, string text, float left, float top, float width, float height, int fontSize)
        {
            GameObject go = gui.CreateText(text, parent, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, gui.AveriaSerifBold, fontSize, gui.ValheimOrange, true, Color.black, width, height, false);
            var rect = (RectTransform)go.transform;
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            Text label = go.GetComponent<Text>();
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            return label;
        }

        public static Button CornerDismiss(GUIManager gui, Transform root, UnityAction close)
        {
            GameObject button = gui.CreateButton(Localization.instance.Localize("$haugbui_panel_dismiss"), root,
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), 36f, 36f);
            Button component = button.GetComponent<Button>();
            component.onClick.AddListener(close);
            button.transform.SetAsLastSibling();
            return component;
        }

        /// <summary>Top-left of the panel is (0, 0). <paramref name="top"/> grows downward.</summary>
        public static ScrollView Scroll(GUIManager gui, Transform parent, float left, float top, float width, float height, Color background)
        {
            Sprite sprite = SpriteOf(gui, parent);
            var root = new GameObject("scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            root.transform.SetParent(parent, false);
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(left, -top);
            rootRect.sizeDelta = new Vector2(width, height);
            Image plate = root.GetComponent<Image>();
            plate.sprite = sprite;
            plate.type = Image.Type.Sliced;
            plate.color = background;
            plate.raycastTarget = true;

            var viewport = new GameObject("viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root.transform, false);
            var viewRect = (RectTransform)viewport.transform;
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(ScrollInset, ScrollInset);
            viewRect.offsetMax = new Vector2(-14f, -4f);
            Image viewImage = viewport.GetComponent<Image>();
            viewImage.sprite = sprite;
            viewImage.color = new Color(1f, 1f, 1f, 0f);
            viewImage.raycastTarget = true;

            var content = new GameObject("content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = new Vector2(0f, 0f);

            Scrollbar bar = Bar(sprite, root.transform);
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            return new ScrollView(root, contentRect, scroll);
        }

        public static void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(content.GetChild(i).gameObject);
        }

        public static void Stack(RectTransform content, float rowHeight)
        {
            float y = 0f;
            for (int i = 0; i < content.childCount; i++)
            {
                var rect = (RectTransform)content.GetChild(i);
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(0f, rowHeight);
                rect.anchoredPosition = new Vector2(0f, -y);
                y += rowHeight;
            }
            content.sizeDelta = new Vector2(0f, y);
        }

        private static Scrollbar Bar(Sprite sprite, Transform parent)
        {
            var root = new GameObject("bar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.sizeDelta = new Vector2(10f, 0f);
            rect.anchoredPosition = new Vector2(-2f, 0f);
            Image image = root.GetComponent<Image>();
            image.sprite = sprite;
            image.color = new Color(0f, 0f, 0f, 0.35f);

            var sliding = new GameObject("sliding", typeof(RectTransform));
            sliding.transform.SetParent(root.transform, false);
            var slidingRect = (RectTransform)sliding.transform;
            slidingRect.anchorMin = Vector2.zero;
            slidingRect.anchorMax = Vector2.one;
            slidingRect.sizeDelta = Vector2.zero;

            var handle = new GameObject("handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(sliding.transform, false);
            var handleRect = (RectTransform)handle.transform;
            handleRect.sizeDelta = Vector2.zero;
            Image handleImage = handle.GetComponent<Image>();
            handleImage.sprite = sprite;
            handleImage.color = new Color(0.9f, 0.72f, 0.4f, 0.95f);

            Scrollbar bar = root.GetComponent<Scrollbar>();
            bar.handleRect = handleRect;
            bar.targetGraphic = handleImage;
            bar.direction = Scrollbar.Direction.BottomToTop;
            return bar;
        }

        private static Sprite SpriteOf(GUIManager gui, Transform parent)
        {
            if (_sprite != null) return _sprite;
            GameObject probe = gui.CreateButton(" ", parent, Vector2.zero, Vector2.zero, Vector2.zero, 4f, 4f);
            _sprite = probe.GetComponent<Image>().sprite;
            UnityEngine.Object.DestroyImmediate(probe);
            return _sprite;
        }
    }
}
