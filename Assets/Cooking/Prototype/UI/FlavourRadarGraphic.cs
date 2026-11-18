using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// uGUI radar chart: dishes as polygons or as a single dot (<see cref="FlavourDot"/>), and zones
    /// as the wedges the judge checks that dot against (or, for a zone without a chart shape, a blob
    /// over the axes it asks for).
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
            public bool Dashed;
            /// <summary>Drawn as a single dot at the shape's centre (a dish or an order), or a dashed ring there (a preview).</summary>
            public DotStyle Dot;
        }

        public enum DotStyle
        {
            None,
            Dot,
            Ring,
        }

        [SerializeField] private float labelMargin = 44f;
        [SerializeField] private int rings = 4;
        [SerializeField] private Color gridColor = new Color(1f, 1f, 1f, 0.14f);

        /// <summary>The desirable zones are drawn in blue unless a colour is given.</summary>
        public static readonly Color DefaultZoneColor = new Color(0.3f, 0.55f, 0.95f, 1f);

        private readonly List<FlavourZone> zones = new List<FlavourZone>();
        private readonly List<Color> zoneColors = new List<Color>();
        private readonly List<Series> series = new List<Series>();
        private FlavourZone highlighted;
        private Text[] labels;

        private float Radius => Mathf.Max(8f, Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f - labelMargin);
        private Vector2 Center => rectTransform.rect.center;

        public void SetZones(IEnumerable<FlavourZone> newZones, FlavourZone highlight = null)
        {
            zones.Clear();
            zoneColors.Clear();
            if(newZones != null) foreach(FlavourZone zone in newZones) AddZone(zone, DefaultZoneColor);
            highlighted = highlight;
            SetVerticesDirty();
        }

        /// <summary>Adds one zone, drawn in <paramref name="zoneColor"/>.</summary>
        public void AddZone(FlavourZone zone, Color zoneColor)
        {
            if(zone == null) return;
            zones.Add(zone);
            zoneColors.Add(zoneColor);
            SetVerticesDirty();
        }

        /// <summary>Room left around the chart for the axis labels (smaller for a small radar).</summary>
        public void SetLabelMargin(float margin)
        {
            labelMargin = margin;
            LayoutLabels();
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

        public void AddProfile(FlavourProfile profile, Color profileColor, bool filled, bool dashed = false)
        {
            series.Add(new Series { Profile = profile, Color = profileColor, Filled = filled, Dashed = dashed });
            SetVerticesDirty();
        }

        /// <summary>
        /// Adds a profile drawn as one point: the centre of its shape on the chart. A dish reads as
        /// a dot that's either inside a tasty zone or not. <paramref name="ring"/> draws a dashed ring
        /// there instead (where a dish would go).
        /// </summary>
        public void AddDot(FlavourProfile profile, Color dotColor, bool ring = false)
        {
            series.Add(new Series { Profile = profile, Color = dotColor, Dot = ring ? DotStyle.Ring : DotStyle.Dot });
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

        private static Vector2 Direction(float axisIndex)
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

            for(int z = 0; z < zones.Count; z++)
            {
                if(zones[z] != null) DrawZone(vh, zones[z], zoneColors[z], zones[z] == highlighted);
            }

            // Shapes first, then dots on top of everything.
            foreach(Series entry in series) if(entry.Dot == DotStyle.None) DrawProfile(vh, entry);
            foreach(Series entry in series) if(entry.Dot != DotStyle.None) DrawProfile(vh, entry);
        }

        /// <summary>
        /// A zone as a blob over the axes it actually asks for (a minimum above 0); axes it doesn't
        /// care about are skipped, so zones stay narrow and keep apart. Between neighbouring axes it
        /// spans the zone's min..max band; at each end it tapers to a point part way to the next
        /// spoke. A zone that asks for nothing in particular is drawn on every axis.
        /// </summary>
        private void DrawZone(VertexHelper vh, FlavourZone zone, Color zoneColor, bool isHighlighted)
        {
            if(zone.HasChartShape)
            {
                DrawWedge(vh, zone, zoneColor, isHighlighted);
                return;
            }

            int count = FlavourAxes.Count;
            Color fill = zoneColor;
            fill.a = highlighted == null ? 0.28f : isHighlighted ? 0.38f : 0.05f;
            Color line = zoneColor;
            line.a = highlighted == null || isHighlighted ? 0.9f : 0.25f;
            float width = isHighlighted ? 3f : 2f;

            FlavourProfile min = zone.Min;
            FlavourProfile max = zone.Max;

            List<int> axes = new List<int>();
            for(int i = 0; i < count; i++) if(Mathf.Min(Value(min, i), Value(max, i)) > 0f) axes.Add(i);
            if(axes.Count == 0) for(int i = 0; i < count; i++) axes.Add(i);

            // Start just after the widest run of skipped axes, so the blob goes the short way round.
            int start = 0;
            if(axes.Count < count)
            {
                int widest = -1;
                for(int k = 0; k < axes.Count; k++)
                {
                    int next = axes[(k + 1) % axes.Count];
                    int gap = (next - axes[k] + count) % count;
                    if(gap == 0) gap = count;
                    if(gap > widest)
                    {
                        widest = gap;
                        start = (k + 1) % axes.Count;
                    }
                }
            }

            List<float> positions = new List<float>();
            for(int k = 0; k < axes.Count; k++)
            {
                float position = axes[(start + k) % axes.Count];
                // Keep the positions increasing as it wraps past the top.
                while(positions.Count > 0 && position <= positions[positions.Count - 1]) position += count;
                positions.Add(position);
            }

            bool closed = axes.Count == count;
            List<Vector2> inner = new List<Vector2>();
            List<Vector2> outer = new List<Vector2>();

            foreach(float position in positions)
            {
                int axis = Mathf.RoundToInt(position) % count;
                float lo = Mathf.Min(Value(min, axis), Value(max, axis));
                float hi = Mathf.Max(Value(min, axis), Value(max, axis));
                inner.Add(PointAt(position, lo));
                outer.Add(PointAt(position, hi));
            }

            for(int k = 0; k + 1 < positions.Count; k++)
            {
                AddQuad(vh, inner[k], outer[k], outer[k + 1], inner[k + 1], fill);
                AddLine(vh, outer[k], outer[k + 1], width, line);
                AddLine(vh, inner[k], inner[k + 1], width, line);
            }

            if(closed)
            {
                int last = positions.Count - 1;
                AddQuad(vh, inner[last], outer[last], outer[0], inner[0], fill);
                AddLine(vh, outer[last], outer[0], width, line);
                AddLine(vh, inner[last], inner[0], width, line);
                return;
            }

            // Rounded-off ends: a tip part way to the next spoke, at the middle of the band.
            AddTip(vh, positions[0], -0.4f, min, max, inner[0], outer[0], fill, line, width);
            int end = positions.Count - 1;
            AddTip(vh, positions[end], 0.4f, min, max, inner[end], outer[end], fill, line, width);
        }

        private void AddTip(VertexHelper vh, float position, float offset, FlavourProfile min, FlavourProfile max, Vector2 inner, Vector2 outer,
            Color fill, Color line, float width)
        {
            int axis = Mathf.RoundToInt(position) % FlavourAxes.Count;
            float middle = (Value(min, axis) + Value(max, axis)) * 0.5f;
            Vector2 tip = PointAt(position + offset, middle);
            AddTriangle(vh, inner, outer, tip, fill);
            AddLine(vh, outer, tip, width, line);
            AddLine(vh, inner, tip, width, line);
        }

        /// <summary>A point at a fractional axis position (between two spokes), for a zone's rounded ends.</summary>
        private Vector2 PointAt(float axisPosition, float value)
        {
            float t = Mathf.Clamp(value, 0f, FlavourProfile.MaxValue) / FlavourProfile.MaxValue;
            return Center + Direction(axisPosition) * (Radius * t);
        }

        /// <summary>
        /// The zone's shape on the chart: a wedge (or a disc in the middle), exactly the area the
        /// judge checks a dish's dot against (<see cref="FlavourDot"/>).
        /// </summary>
        private void DrawWedge(VertexHelper vh, FlavourZone zone, Color zoneColor, bool isHighlighted)
        {
            Color fill = zoneColor;
            fill.a = highlighted == null ? 0.3f : isHighlighted ? 0.4f : 0.06f;
            Color line = zoneColor;
            line.a = highlighted == null || isHighlighted ? 0.9f : 0.25f;
            float width = isHighlighted ? 3f : 2f;

            int count = FlavourAxes.Count;
            bool ring = zone.ChartSpread * 2f >= count;
            float from = ring ? 0f : zone.ChartAxis - zone.ChartSpread;
            float to = ring ? count : zone.ChartAxis + zone.ChartSpread;
            int steps = Mathf.Max(6, Mathf.CeilToInt((to - from) * 8f));
            float inner = zone.ChartInner * Radius;
            float outer = zone.ChartOuter * Radius;

            for(int k = 0; k < steps; k++)
            {
                float a = Mathf.Lerp(from, to, k / (float)steps);
                float b = Mathf.Lerp(from, to, (k + 1) / (float)steps);
                Vector2 innerA = Center + FlavourDot.Direction(a) * inner;
                Vector2 innerB = Center + FlavourDot.Direction(b) * inner;
                Vector2 outerA = Center + FlavourDot.Direction(a) * outer;
                Vector2 outerB = Center + FlavourDot.Direction(b) * outer;
                AddQuad(vh, innerA, outerA, outerB, innerB, fill);
                AddLine(vh, outerA, outerB, width, line);
                if(inner > 0.5f) AddLine(vh, innerA, innerB, width, line);
            }

            if(ring) return;

            // The straight sides.
            AddLine(vh, Center + FlavourDot.Direction(from) * inner, Center + FlavourDot.Direction(from) * outer, width, line);
            AddLine(vh, Center + FlavourDot.Direction(to) * inner, Center + FlavourDot.Direction(to) * outer, width, line);
        }

        /// <summary>Where a profile's dot goes on the chart (the same point the judge uses).</summary>
        private Vector2 Centroid(FlavourProfile profile)
        {
            return Center + FlavourDot.Of(profile) * Radius;
        }

        private void DrawDot(VertexHelper vh, Series entry)
        {
            Vector2 centre = Centroid(entry.Profile);
            float radius = Mathf.Clamp(Radius * 0.06f, 3.5f, 8f);
            const int segments = 20;

            if(entry.Dot == DotStyle.Ring)
            {
                // Dashed: every other segment of a circle.
                for(int k = 0; k < segments; k += 2)
                {
                    Vector2 a = centre + Direction(k * FlavourAxes.Count / (float)segments) * (radius * 1.6f);
                    Vector2 b = centre + Direction((k + 1) * FlavourAxes.Count / (float)segments) * (radius * 1.6f);
                    AddLine(vh, a, b, 2.5f, entry.Color);
                }

                return;
            }

            // A dark rim, then the dot.
            Color rim = new Color(0.05f, 0.05f, 0.06f, Mathf.Min(1f, entry.Color.a + 0.2f));
            for(int k = 0; k < segments; k++)
            {
                Vector2 a = centre + Direction(k * FlavourAxes.Count / (float)segments) * (radius + 2f);
                Vector2 b = centre + Direction((k + 1) * FlavourAxes.Count / (float)segments) * (radius + 2f);
                AddTriangle(vh, centre, a, b, rim);
            }

            for(int k = 0; k < segments; k++)
            {
                Vector2 a = centre + Direction(k * FlavourAxes.Count / (float)segments) * radius;
                Vector2 b = centre + Direction((k + 1) * FlavourAxes.Count / (float)segments) * radius;
                AddTriangle(vh, centre, a, b, entry.Color);
            }
        }

        private static void AddDashedLine(VertexHelper vh, Vector2 a, Vector2 b, float width, Color lineColor, float dash = 7f, float gap = 5f)
        {
            float length = Vector2.Distance(a, b);
            if(length < 0.01f) return;

            Vector2 dir = (b - a) / length;

            for(float d = 0f; d < length; d += dash + gap)
            {
                AddLine(vh, a + dir * d, a + dir * Mathf.Min(d + dash, length), width, lineColor);
            }
        }

        private void DrawProfile(VertexHelper vh, Series entry)
        {
            if(entry.Dot != DotStyle.None)
            {
                DrawDot(vh, entry);
                return;
            }

            int count = FlavourAxes.Count;

            if(entry.Filled)
            {
                Color fill = entry.Color;
                fill.a *= 0.3f;
                for(int i = 0; i < count; i++) AddTriangle(vh, Center, Point(i, Value(entry.Profile, i)), Point(i + 1, Value(entry.Profile, i + 1)), fill);
            }

            for(int i = 0; i < count; i++)
            {
                Vector2 a = Point(i, Value(entry.Profile, i));
                Vector2 b = Point(i + 1, Value(entry.Profile, i + 1));
                if(entry.Dashed) AddDashedLine(vh, a, b, 2.5f, entry.Color, 5f, 4f);
                else AddLine(vh, a, b, entry.Filled ? 3f : 2f, entry.Color);
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
