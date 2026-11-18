using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.UI
{
    /// <summary>
    /// Small helpers for building screens in code: a canvas, flat boxes, text and visor line work
    /// (<see cref="VisorGraphic"/>). Positions are in a 1920 x 1080 layout that scales with the screen.
    /// Every screen uses these, so the look changes in one place.
    /// </summary>
    public static class UIKit
    {
        const int UILayer = 5;
        static Font font;

        /// <summary>
        /// The display font: a squared, instrument-style face installed with the system when there is one
        /// (Bahnschrift ships with Windows 10 and 11; nothing is bundled), else Unity's built-in font.
        /// </summary>
        static readonly string[] DisplayFonts = { "Bahnschrift", "Bahnschrift SemiBold", "DIN Alternate", "Roboto Condensed", "DejaVu Sans Condensed" };

        public static Font Font
        {
            get
            {
                if (font == null) font = PickFont();
                return font;
            }
        }

        static Font PickFont()
        {
            try
            {
                var installed = Font.GetOSInstalledFontNames();
                if (installed != null)
                    foreach (var want in DisplayFonts)
                        if (System.Array.IndexOf(installed, want) >= 0)
                        {
                            var os = Font.CreateDynamicFontFromOSFont(want, 32);
                            if (os != null) return os;
                        }
            }
            catch (System.Exception)
            {
                // Some platforms can't list system fonts; the built-in font is fine.
            }
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            // Snap text to whole screen pixels so it stays sharp.
            canvas.pixelPerfect = true;
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

        /// <summary>
        /// Keep a camera canvas just past the camera's near clip plane, in front of everything else the
        /// camera draws: the gun in the player's hands sits closer than the old 0.3 m and covered the ammo.
        /// The canvas fills the view at any field of view, so zooming or scoping in keeps it whole. Call
        /// every frame: the camera can change.
        /// </summary>
        public static void KeepInFront(Canvas canvas)
        {
            if (canvas.worldCamera == null) canvas.worldCamera = Camera.main;
            var cam = canvas.worldCamera;
            if (cam == null) return;
            float distance = cam.nearClipPlane * 1.02f + 0.001f;
            if (Mathf.Abs(canvas.planeDistance - distance) > 1e-5f) canvas.planeDistance = distance;
        }

        /// <summary>
        /// A 1920 x 1080 layer centred on <paramref name="parent"/>. Helmet-display parts hang from it and
        /// are placed in its pixels, (0, 0) bottom left to (1920, 1080) top right.
        /// </summary>
        public static RectTransform Layer(Transform parent, string name) =>
            Rect(parent, name, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920f, 1080f));

        /// <summary>Line work covering a whole <see cref="Layer"/>; its shapes use the layer's pixels.</summary>
        public static VisorGraphic Lines(Transform layer, string name)
        {
            var rect = Rect(layer, name, Vector2.zero, Vector2.zero, new Vector2(1920f, 1080f));
            // The renderer first: a Graphic draws nothing without one.
            var renderer = rect.gameObject.AddComponent<CanvasRenderer>();
            // Glow edges fade to nothing; never let the renderer skip a mesh for its clear corners.
            renderer.cullTransparentMesh = false;
            var graphic = rect.gameObject.AddComponent<VisorGraphic>();
            graphic.raycastTarget = false;
            return graphic;
        }

        /// <summary>
        /// Text centred on <paramref name="centre"/> in a <see cref="Layer"/>'s pixels, with a soft dark
        /// edge so it reads over a bright sky without a box behind it.
        /// </summary>
        /// <summary>Text smaller than this is never bold, so small labels stay sharp.</summary>
        const int BoldFromSize = 24;

        public static Text Readout(Transform layer, string name, Vector2 centre, Vector2 size, string text, int fontSize, Color color, TextAnchor align, bool bold = false)
        {
            var label = Label(layer, name, Vector2.zero, Vector2.zero, size, text, fontSize, color, align);
            var rect = label.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = centre;
            // Bold is drawn by thickening the glyphs, which runs small letters together; keep it for big text.
            if (bold && fontSize >= BoldFromSize) label.fontStyle = FontStyle.Bold;
            // One crisp drop shadow, not an outline: four smeared copies made small text look blurry.
            var edge = label.gameObject.AddComponent<Shadow>();
            edge.effectColor = new Color(0f, 0.02f, 0.04f, 0.85f);
            edge.effectDistance = new Vector2(1f, -1f);
            return label;
        }
    }
}
