using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// Small helpers for building screens in code: a canvas, flat boxes and text. Positions are in
    /// a 1920 x 1080 layout that scales with the screen. Every screen uses these, so the look
    /// changes in one place.
    /// </summary>
    public static class UIKit
    {
        const int UILayer = 5;
        static Font font;

        public static Font Font
        {
            get
            {
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        /// <summary>
        /// A canvas drawn by the main camera (so screenshots taken from that camera include it),
        /// laid out at 1920 x 1080.
        /// </summary>
        public static Canvas Canvas(string name, Transform parent, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera.main;
            canvas.planeDistance = 0.3f;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>
        /// An empty rectangle. <paramref name="anchor"/> is the point of the parent it hangs from
        /// (0,0 bottom left to 1,1 top right) and also its own pivot; <paramref name="offset"/> moves
        /// it from there in layout pixels.
        /// </summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Box(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchor, offset, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, string text, int fontSize, Color color, TextAnchor align)
        {
            var rect = Rect(parent, name, anchor, offset, size);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = align;
            label.text = text;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        /// <summary>A bar that fills from the left: a dark back and a coloured fill. Returns the fill; set its width with <see cref="SetFill"/>.</summary>
        public static RectTransform Bar(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, Color color)
        {
            var back = Box(parent, name, anchor, offset, size, new Color(0f, 0f, 0f, 0.55f));
            var fill = Box(back.transform, "Fill", new Vector2(0f, 0.5f), Vector2.zero, size, color);
            return fill.rectTransform;
        }

        public static void SetFill(RectTransform fill, float fullWidth, float share)
        {
            fill.sizeDelta = new Vector2(fullWidth * Mathf.Clamp01(share), fill.sizeDelta.y);
        }
    }
}
