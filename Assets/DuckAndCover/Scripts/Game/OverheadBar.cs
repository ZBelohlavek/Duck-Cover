using UnityEngine;
using UnityEngine.UI;

namespace DuckAndCover.Game
{
    /// <summary>
    /// The bar over a hero's head, in the helmet display's style: a thin dark glass strip with the health
    /// fill, green for this player's team and red for the other, and the hero's level in a framed chip at
    /// its left in the same colour. Health only: heroes carry no shields (taken out Nov 19, 2026). Drawn
    /// on a small world-space canvas that turns to face the camera; colours are UI vertex colours, not
    /// material colours, so the <see cref="Paint"/> rule doesn't apply.
    /// </summary>
    public class OverheadBar : MonoBehaviour
    {
        /// <summary>Health for this player's team and for the other (the HUD's own health green and danger red).</summary>
        public static readonly Color Ally = new Color(0.40f, 0.95f, 0.55f);
        public static readonly Color Enemy = new Color(1.00f, 0.32f, 0.28f);
        static readonly Color Glass = new Color(0.01f, 0.04f, 0.07f, 0.72f);
        static readonly Color Missing = new Color(1f, 1f, 1f, 0.10f);

        // Layout in canvas pixels; the canvas is scaled so PixelsPerMetre of them make one metre.
        const float PixelsPerMetre = 400f;
        const float Height = 44f;
        const float Chip = 60f;
        const float Gap = 8f;
        const float Edge = 3f;

        Image fill, chipFrame, barFrame;
        Text level;
        float fillWidth;
        int shownLevel = -1;
        int shownSide = -1;

        /// <summary>A bar <paramref name="width"/> metres long (with the level chip), centred over <paramref name="parent"/> at <paramref name="localPosition"/>.</summary>
        public static OverheadBar Create(Transform parent, Vector3 localPosition, float width, int layer)
        {
            var go = new GameObject("Overhead", typeof(RectTransform));
            go.layer = layer;
            var root = go.GetComponent<RectTransform>();
            root.SetParent(parent, false);
            root.localPosition = localPosition;
            root.localScale = Vector3.one * (1f / PixelsPerMetre);
            float total = width * PixelsPerMetre;
            root.sizeDelta = new Vector2(total, Chip);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var bar = go.AddComponent<OverheadBar>();
            bar.Build(root, total, layer);
            return bar;
        }

        void Build(RectTransform root, float total, int layer)
        {
            float barWidth = total - Chip - Gap;
            float barX = -total * 0.5f + Chip + Gap;

            // Level chip: a frame in the side's colour around dark glass, the number in that colour.
            chipFrame = Box(root, "LevelFrame", new Vector2(-total * 0.5f, 0f), new Vector2(Chip, Chip), Ally, layer);
            Box(chipFrame.rectTransform, "LevelGlass", new Vector2(Edge, 0f), new Vector2(Chip - Edge * 2f, Chip - Edge * 2f), Glass, layer);
            level = Label(chipFrame.rectTransform, "Level", layer);

            // Health: a frame, dark glass, what's missing faintly, and the fill from the left.
            barFrame = Box(root, "BarFrame", new Vector2(barX, 0f), new Vector2(barWidth, Height), Ally, layer);
            var glass = Box(barFrame.rectTransform, "BarGlass", new Vector2(Edge, 0f), new Vector2(barWidth - Edge * 2f, Height - Edge * 2f), Glass, layer);
            fillWidth = barWidth - Edge * 4f;
            Box(glass.rectTransform, "Missing", new Vector2(Edge, 0f), new Vector2(fillWidth, Height - Edge * 4f), Missing, layer);
            fill = Box(glass.rectTransform, "Health", new Vector2(Edge, 0f), new Vector2(fillWidth, Height - Edge * 4f), Ally, layer);
            barFrame.color = WithAlpha(Ally, 0.55f);
        }

        /// <summary>A box whose left edge sits at <paramref name="at"/>.x, centred on its parent's middle at .y.</summary>
        static Image Box(RectTransform parent, string name, Vector2 at, Vector2 size, Color color, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            bool root = parent.GetComponent<Canvas>() != null;
            // Children of the root hang from its centre; children of a box from the box's left edge.
            var anchor = root ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 0.5f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = at;
            rect.sizeDelta = size;
            go.AddComponent<CanvasRenderer>();
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static Text Label(RectTransform parent, string name, int layer)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            go.AddComponent<CanvasRenderer>();
            var text = go.AddComponent<Text>();
            text.font = DisplayFont.Font;
            text.fontSize = 44;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.color = Ally;
            return text;
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

        /// <summary>
        /// Health as a share of the maximum (0..1), whether the hero is on this player's team, their level
        /// (0 hides the chip's number), and turn the bar to face a camera.
        /// </summary>
        public void Show(float health, bool friendly, int heroLevel, Camera viewer)
        {
            int side = friendly ? 1 : 0;
            if (side != shownSide)
            {
                shownSide = side;
                Color c = friendly ? Ally : Enemy;
                fill.color = c;
                chipFrame.color = c;
                level.color = c;
                barFrame.color = WithAlpha(c, 0.55f);
            }
            if (heroLevel != shownLevel)
            {
                shownLevel = heroLevel;
                level.text = heroLevel > 0 ? heroLevel.ToString() : "";
            }
            var rect = fill.rectTransform;
            rect.sizeDelta = new Vector2(Mathf.Max(0f, fillWidth * Mathf.Clamp01(health)), rect.sizeDelta.y);
            // Face the camera the way a sign does: the canvas's front looks back at the viewer.
            if (viewer != null) transform.rotation = Quaternion.LookRotation(transform.position - viewer.transform.position);
        }
    }
}
