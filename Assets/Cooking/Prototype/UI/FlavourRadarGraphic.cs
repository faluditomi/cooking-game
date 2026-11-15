using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// uGUI radar chart: zones as shaded min..max bands, dishes as polygons with a dot per spoke.
    /// Runtime counterpart of the editor's FlavourRadarGUI.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class FlavourRadarGraphic : MaskableGraphic
    {
        private struct Series
        {
            public FlavourProfile Profile;
            public Color Color;
            public bool Filled;
        }

        [SerializeField] private float labelMargin = 44f;
        [SerializeField] private int rings = 4;
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.14f);

        private readonly List<FlavourZone> zones = new List<FlavourZone>();
        private readonly List<Series> series = new List<Series>();
        private FlavourZone highlighted;
        private Text[] labels;

        private float Radius => Mathf.Max(8f, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f - labelMargin);
        private Vector2 Center => rectTransform.rect.center;

        public void SetZones(IEnumerable<FlavourZone> newZones, FlavourZone highlight = null)
        {
            zones.Clear();
            if(newZones != null) zones.AddRange(newZones);
            highlighted = highlight;
            SetVerticesDirty();
        }

        public void SetHighlight(FlavourZone highlight)
        {
            highlighted = highlight;
            SetVerticesDirty();
        }

        public void ClearProfiles()
        {
            series.Clear();
            SetVerticesDirty();
        }

        public void AddProfile(FlavourProfile profile, Color profileColor, bool filled)
        {
            series.Add(new Series { Profile = profile, Color = profileColor, Filled = filled });
            SetVerticesDirty();
        }

        /// <summary>Creates the eight axis labels as child Texts.</summary>
        public void CreateLabels(Font font, int fontSize, Color labelColor)
        {
            if(labels != null) return;

            labels = new Text[FlavourAxes.Count];

            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                GameObject go = new GameObject("Label " + FlavourAxes.All[i], typeof(RectTransform));
                go.transform.SetParent(transform, false);
                Text text = go.AddComponent<Text>();
                text.font = font;
                text.fontSize = fontSize;
                text.color = labelColor;
                text.alignment = TextAnchor.MiddleCenter;
                text.raycastTarget = false;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.text = FlavourAxes.Label(FlavourAxes.All[i]);
                text.rectTransform.sizeDelta = new Vector2(110f, 24f);
                labels[i] = text;
            }

            LayoutLabels();
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            LayoutLabels();
        }

        private void LayoutLabels()
        {
            if(labels == null) return;

            for(int i = 0; i < labels.Length; i++)
            {
                if(labels[i] == null) continue;
                labels[i].rectTransform.localPosition = Center + Direction(i) * (Radius + labelMargin * 0.5f);
            }
        }

        private static Vector2 Direction(int axisIndex)
        {
            // Clockwise from the top, same order as the editor chart.
            float angle = (90f - axisIndex * 360f / FlavourAxes.Count) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        private Vector2 Point(int axisIndex, float value)
        {
            float t = Mathf.Clamp(value, 0f, FlavourProfile.MaxValue) / FlavourProfile.MaxValue;
            return Center + Direction(axisIndex % FlavourAxes.Count) * (Radius * t);
        }

        private float Value(FlavourProfile profile, int axisIndex)
        {
            return profile[FlavourAxes.All[axisIndex % FlavourAxes.Count]];
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int count = FlavourAxes.Count;

            for(int r = 1; r <= rings; r++)
            {
                float value = FlavourProfile.MaxValue * r / rings;
                for(int i = 0; i < count; i++) AddLine(vh, Point(i, value), Point(i + 1, value), 1.5f, gridColor);
            }

            for(int i = 0; i < count; i++) AddLine(vh, Center, Point(i, FlavourProfile.MaxValue), 1.5f, gridColor);

            foreach(FlavourZone zone in zones)
            {
                if(zone != null) DrawZone(vh, zone, zone == highlighted);
            }

            foreach(Series entry in series) DrawProfile(vh, entry);
        }

        private void DrawZone(VertexHelper vh, FlavourZone zone, bool isHighlighted)
        {
            int count = FlavourAxes.Count;
            Color fill = zone.Color;
            fill.a = highlighted == null ? 0.16f : isHighlighted ? 0.32f : 0.03f;
            Color line = zone.Color;
            line.a = highlighted == null || isHighlighted ? 0.85f : 0.2f;

            FlavourProfile min = zone.Min;
            FlavourProfile max = zone.Max;

            for(int i = 0; i < count; i++)
            {
                Vector2 innerA = Point(i, Mathf.Min(Value(min, i), Value(max, i)));
                Vector2 outerA = Point(i, Mathf.Max(Value(min, i), Value(max, i)));
                Vector2 innerB = Point(i + 1, Mathf.Min(Value(min, i + 1), Value(max, i + 1)));
                Vector2 outerB = Point(i + 1, Mathf.Max(Value(min, i + 1), Value(max, i + 1)));
                AddQuad(vh, innerA, outerA, outerB, innerB, fill);
                AddLine(vh, outerA, outerB, isHighlighted ? 3f : 2f, line);
            }
        }

        private void DrawProfile(VertexHelper vh, Series entry)
        {
            int count = FlavourAxes.Count;

            if(entry.Filled)
            {
                Color fill = entry.Color;
                fill.a *= 0.3f;
                for(int i = 0; i < count; i++) AddTriangle(vh, Center, Point(i, Value(entry.Profile, i)), Point(i + 1, Value(entry.Profile, i + 1)), fill);
            }

            for(int i = 0; i < count; i++)
            {
                AddLine(vh, Point(i, Value(entry.Profile, i)), Point(i + 1, Value(entry.Profile, i + 1)), entry.Filled ? 3f : 2f, entry.Color);
            }

            for(int i = 0; i < count; i++)
            {
                Vector2 p = Point(i, Value(entry.Profile, i));
                float s = entry.Filled ? 5f : 3.5f;
                AddQuad(vh, p + new Vector2(-s, -s), p + new Vector2(-s, s), p + new Vector2(s, s), p + new Vector2(s, -s), entry.Color);
            }
        }

        private static void AddLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color lineColor)
        {
            Vector2 dir = b - a;
            if(dir.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            AddQuad(vh, a - n, a + n, b + n, b - n, lineColor);
        }

        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color quadColor)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, quadColor, Vector2.zero);
            vh.AddVert(b, quadColor, Vector2.zero);
            vh.AddVert(c, quadColor, Vector2.zero);
            vh.AddVert(d, quadColor, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color triColor)
        {
            int start = vh.currentVertCount;
            vh.AddVert(a, triColor, Vector2.zero);
            vh.AddVert(b, triColor, Vector2.zero);
            vh.AddVert(c, triColor, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }
    }
}
