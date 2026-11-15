using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cooking.EditorTools
{
    /// <summary>
    /// IMGUI radar chart for flavour profiles and zones. Zones draw as shaded min..max bands;
    /// dishes draw as an outline with a dot on each spoke.
    /// </summary>
    public static class FlavourRadarGUI
    {
        private const float LabelMargin = 34f;
        private const int Rings = 4;

        /// <summary>Reserves a square-ish layout rect and draws the chart into it.</summary>
        public static void Layout(float height, IList<FlavourZone> zones, IList<(FlavourProfile profile, Color color)> profiles)
        {
            Rect rect = GUILayoutUtility.GetRect(height, height, GUILayout.ExpandWidth(true));
            Draw(rect, zones, profiles);
        }

        public static void Draw(Rect rect, IList<FlavourZone> zones, IList<(FlavourProfile profile, Color color)> profiles)
        {
            if(Event.current.type != EventType.Repaint) return;

            Vector2 center = rect.center;
            float radius = Mathf.Max(8f, Mathf.Min(rect.width, rect.height) * 0.5f - LabelMargin);

            DrawGrid(center, radius);

            if(zones != null)
            {
                foreach(FlavourZone zone in zones)
                {
                    if(zone != null) DrawZone(center, radius, zone);
                }
            }

            if(profiles != null)
            {
                foreach((FlavourProfile profile, Color color) entry in profiles)
                {
                    DrawProfile(center, radius, entry.profile, entry.color);
                }
            }

            DrawLabels(center, radius);
        }

        private static Vector3 Point(Vector2 center, float radius, int axisIndex, float value)
        {
            float angle = (-90f + axisIndex * 360f / FlavourAxes.Count) * Mathf.Deg2Rad;
            float t = Mathf.Clamp(value, 0f, FlavourProfile.MaxValue) / FlavourProfile.MaxValue;
            return new Vector3(center.x + Mathf.Cos(angle) * radius * t, center.y + Mathf.Sin(angle) * radius * t, 0f);
        }

        private static void DrawGrid(Vector2 center, float radius)
        {
            Color grid = EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.12f) : new Color(0f, 0f, 0f, 0.15f);
            Handles.color = grid;

            Vector3[] ring = new Vector3[FlavourAxes.Count + 1];

            for(int r = 1; r <= Rings; r++)
            {
                float value = FlavourProfile.MaxValue * r / Rings;

                for(int i = 0; i <= FlavourAxes.Count; i++)
                {
                    ring[i] = Point(center, radius, i % FlavourAxes.Count, value);
                }

                Handles.DrawAAPolyLine(1.5f, ring);
            }

            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                Handles.DrawAAPolyLine(1.5f, new Vector3(center.x, center.y, 0f), Point(center, radius, i, FlavourProfile.MaxValue));
            }
        }

        private static void DrawZone(Vector2 center, float radius, FlavourZone zone)
        {
            Color fill = zone.Color;
            fill.a = 0.22f;
            Handles.color = fill;

            FlavourProfile min = zone.Min;
            FlavourProfile max = zone.Max;

            // Band between the min and max polygons, drawn as two triangles per spoke pair so
            // concave shapes still render.
            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                int j = (i + 1) % FlavourAxes.Count;
                FlavourAxis a = FlavourAxes.All[i];
                FlavourAxis b = FlavourAxes.All[j];

                Vector3 innerA = Point(center, radius, i, Mathf.Min(min[a], max[a]));
                Vector3 outerA = Point(center, radius, i, Mathf.Max(min[a], max[a]));
                Vector3 innerB = Point(center, radius, j, Mathf.Min(min[b], max[b]));
                Vector3 outerB = Point(center, radius, j, Mathf.Max(min[b], max[b]));

                Handles.DrawAAConvexPolygon(innerA, outerA, outerB);
                Handles.DrawAAConvexPolygon(innerA, outerB, innerB);
            }

            Color line = zone.Color;
            line.a = 0.8f;
            Handles.color = line;
            Handles.DrawAAPolyLine(2f, Polygon(center, radius, max));
        }

        private static void DrawProfile(Vector2 center, float radius, FlavourProfile profile, Color color)
        {
            Handles.color = color;
            Handles.DrawAAPolyLine(3f, Polygon(center, radius, profile));

            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                Handles.DrawSolidDisc(Point(center, radius, i, profile[FlavourAxes.All[i]]), Vector3.forward, 3.5f);
            }
        }

        private static Vector3[] Polygon(Vector2 center, float radius, FlavourProfile profile)
        {
            Vector3[] points = new Vector3[FlavourAxes.Count + 1];

            for(int i = 0; i <= FlavourAxes.Count; i++)
            {
                int index = i % FlavourAxes.Count;
                points[i] = Point(center, radius, index, profile[FlavourAxes.All[index]]);
            }

            return points;
        }

        private static void DrawLabels(Vector2 center, float radius)
        {
            GUIStyle style = EditorStyles.miniLabel;
            Vector2 size = new Vector2(70f, EditorGUIUtility.singleLineHeight);

            for(int i = 0; i < FlavourAxes.Count; i++)
            {
                Vector3 tip = Point(center, radius + 16f, i, FlavourProfile.MaxValue);
                GUIContent content = new GUIContent(FlavourAxes.Label(FlavourAxes.All[i]));
                Vector2 textSize = style.CalcSize(content);
                Rect labelRect = new Rect(tip.x - textSize.x * 0.5f, tip.y - size.y * 0.5f, textSize.x, size.y);
                GUI.Label(labelRect, content, style);
            }
        }
    }
}
