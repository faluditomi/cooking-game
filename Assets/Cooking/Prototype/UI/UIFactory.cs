using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>Small helpers for building the prototype's uGUI in code, so the scene needs no hand-made UI.</summary>
    public static class UIFactory
    {
        public static readonly Color Background = new Color(0.10f, 0.09f, 0.11f, 1f);
        public static readonly Color PanelColor = new Color(0.16f, 0.15f, 0.18f, 1f);
        public static readonly Color PanelDark = new Color(0.12f, 0.11f, 0.13f, 1f);
        public static readonly Color ButtonColor = new Color(0.26f, 0.24f, 0.29f, 1f);
        public static readonly Color Accent = new Color(0.95f, 0.55f, 0.2f, 1f);
        public static readonly Color TextColor = new Color(0.92f, 0.9f, 0.86f, 1f);
        public static readonly Color MutedText = new Color(0.65f, 0.63f, 0.6f, 1f);
        public static readonly Color Good = new Color(0.45f, 0.8f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.9f, 0.35f, 0.3f, 1f);

        private static Font font;

        public static Font Font
        {
            get
            {
                if(font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        private static Sprite circle;

        /// <summary>A soft-edged white disc, made at runtime (for heads, plates and other round shapes).</summary>
        public static Sprite Circle
        {
            get
            {
                if(circle != null) return circle;

                const int size = 128;
                Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture.wrapMode = TextureWrapMode.Clamp;
                Color32[] pixels = new Color32[size * size];
                float radius = size / 2f;

                for(int y = 0; y < size; y++)
                {
                    for(int x = 0; x < size; x++)
                    {
                        float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                        byte alpha = (byte)(Mathf.Clamp01(radius - distance) * 255f);
                        pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                }

                texture.SetPixels32(pixels);
                texture.Apply();
                circle = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
                return circle;
            }
        }

        /// <summary>A thin rectangular outline (four bars) inside <paramref name="rt"/>. Returns the bars to recolour.</summary>
        public static Image[] Outline(RectTransform rt, Color color, float thickness)
        {
            Image[] bars = new Image[4];
            for(int i = 0; i < 4; i++)
            {
                bars[i] = Panel(rt, "Outline", color);
                bars[i].raycastTarget = false;
                RectTransform bar = bars[i].rectTransform;
                bool horizontal = i < 2;
                bar.anchorMin = horizontal ? new Vector2(0f, i == 0 ? 0f : 1f) : new Vector2(i == 2 ? 0f : 1f, 0f);
                bar.anchorMax = horizontal ? new Vector2(1f, i == 0 ? 0f : 1f) : new Vector2(i == 2 ? 0f : 1f, 1f);
                bar.pivot = horizontal ? new Vector2(0.5f, i == 0 ? 0f : 1f) : new Vector2(i == 2 ? 0f : 1f, 0.5f);
                bar.sizeDelta = horizontal ? new Vector2(0f, thickness) : new Vector2(thickness, 0f);
                bar.anchoredPosition = Vector2.zero;
            }

            return bars;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Anchors <paramref name="rt"/> to a normalized region of its parent, with pixel insets.</summary>
        public static RectTransform Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax, float inset = 0f)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static RectTransform Stretch(RectTransform rt, float inset = 0f)
        {
            return Place(rt, 0f, 0f, 1f, 1f, inset);
        }

        public static Image Panel(Transform parent, string name, Color color)
        {
            RectTransform rt = Rect(parent, name);
            Image image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, string text, int size = 18, TextAnchor anchor = TextAnchor.UpperLeft,
            FontStyle style = FontStyle.Normal, Color? color = null)
        {
            RectTransform rt = Rect(parent, "Text");
            Text label = rt.gameObject.AddComponent<Text>();
            label.font = Font;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = anchor;
            label.color = color ?? TextColor;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, Color? color = null, int fontSize = 18)
        {
            Image image = Panel(parent, "Button " + text, color ?? ButtonColor);
            Button button = image.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            button.colors = colors;
            if(onClick != null) button.onClick.AddListener(onClick);

            Text label = Label(image.transform, text, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 4f);
            return button;
        }

        /// <summary>A square, aspect-preserving image for an ingredient or dish sprite. Clear when there's no sprite.</summary>
        public static Image Icon(Transform parent, Sprite sprite, float size)
        {
            Image image = Panel(parent, "Icon", sprite != null ? Color.white : Color.clear);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            Size(image, size, 0f, size, 0f);
            return image;
        }

        /// <summary>Shrinks the text (down to 12 px) when it would otherwise spill out of its rect.</summary>
        public static void FitText(Text text)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = text.fontSize;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        public static void SetButtonText(Button button, string text)
        {
            Text label = button.GetComponentInChildren<Text>();
            if(label != null) label.text = text;
        }

        public static VerticalLayoutGroup Vertical(GameObject go, float spacing = 8f, int padding = 12)
        {
            VerticalLayoutGroup layout = go.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup Horizontal(GameObject go, float spacing = 8f, int padding = 0)
        {
            HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return layout;
        }

        /// <summary>Adds or updates a LayoutElement. Negative values are left unset.</summary>
        public static LayoutElement Size(Component target, float preferredHeight = -1f, float flexibleHeight = -1f,
            float preferredWidth = -1f, float flexibleWidth = -1f)
        {
            LayoutElement element = target.GetComponent<LayoutElement>();
            if(element == null) element = target.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = preferredHeight;
            element.minHeight = preferredHeight;
            element.flexibleHeight = flexibleHeight;
            element.preferredWidth = preferredWidth;
            element.flexibleWidth = flexibleWidth;
            return element;
        }

        /// <summary>
        /// A vertically scrolling list that fills the remaining height of a vertical layout.
        /// Returns the content transform to add rows to.
        /// </summary>
        public static RectTransform ScrollList(Transform parent, float spacing = 6f)
        {
            RectTransform scroll = Rect(parent, "Scroll List");
            Size(scroll, 60f, 1f);
            // Clear graphic so the mouse wheel scrolls anywhere over the list.
            scroll.gameObject.AddComponent<Image>().color = Color.clear;
            ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            RectTransform viewport = Rect(scroll, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = Rect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            Vertical(content.gameObject, spacing, 0);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport;
            scrollRect.content = content;
            return content;
        }

        /// <summary>Destroys every child, for rebuilding a dynamic list.</summary>
        public static void ClearChildren(Transform parent)
        {
            for(int i = parent.childCount - 1; i >= 0; i--)
            {
                GameObject child = parent.GetChild(i).gameObject;
                child.SetActive(false);
                Object.Destroy(child);
            }
        }

        /// <summary>A row container for buttons inside a vertical layout.</summary>
        public static RectTransform Row(Transform parent, float height, float spacing = 8f)
        {
            RectTransform row = Rect(parent, "Row");
            Horizontal(row.gameObject, spacing);
            // A force-expanding group reports flexible height 1; pin it so rows don't soak up spare space.
            Size(row, height, 0f);
            return row;
        }
    }

    /// <summary>
    /// A horizontal progress bar with coloured bands (undercooked / perfect / danger) and a marker.
    /// </summary>
    public class TimingBar
    {
        private readonly RectTransform marker;
        private readonly RectTransform fill;

        public TimingBar(Transform parent, CookingStation station, float height = 28f)
        {
            Image background = UIFactory.Panel(parent, "Timing Bar", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(background, height);

            Band(background.transform, 0f, station.PerfectStart, new Color(0.35f, 0.35f, 0.4f, 1f));
            Band(background.transform, station.PerfectStart, station.PerfectEnd, new Color(0.3f, 0.6f, 0.3f, 1f));

            if(station.Stirs)
            {
                // Wok: red overcooks a stir, black burns.
                Band(background.transform, station.PerfectEnd, station.BlackStart, new Color(0.7f, 0.25f, 0.2f, 1f));
                Band(background.transform, station.BlackStart, 1f, new Color(0.02f, 0.02f, 0.02f, 1f));
            }
            else
            {
                Band(background.transform, station.DangerStart, 1f, new Color(0.7f, 0.25f, 0.2f, 1f));
            }

            fill = UIFactory.Panel(background.transform, "Fill", new Color(1f, 1f, 1f, 0.18f)).rectTransform;
            UIFactory.Place(fill, 0f, 0f, 0f, 1f);

            marker = UIFactory.Panel(background.transform, "Marker", Color.white).rectTransform;
            marker.anchorMin = new Vector2(0f, -0.15f);
            marker.anchorMax = new Vector2(0f, 1.15f);
            marker.sizeDelta = new Vector2(4f, 0f);
        }

        private static void Band(Transform parent, float from, float to, Color color)
        {
            if(to <= from) return;
            UIFactory.Place(UIFactory.Panel(parent, "Band", color).rectTransform, from, 0f, to, 1f);
        }

        public void Set(float progress, bool visible)
        {
            marker.gameObject.SetActive(visible);
            fill.anchorMax = new Vector2(visible ? progress : 0f, 1f);
            marker.anchorMin = new Vector2(progress, -0.15f);
            marker.anchorMax = new Vector2(progress, 1.15f);
            marker.anchoredPosition = Vector2.zero;
        }
    }
}
