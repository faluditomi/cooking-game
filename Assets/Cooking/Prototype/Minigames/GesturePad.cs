using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// A UI area that reports pointer presses and drags in local space (origin at its centre),
    /// for feeding <see cref="Gesture"/>s. Needs a raycast-target Graphic on the same object.
    /// </summary>
    public class GesturePad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        public event Action<Vector2> Pressed;
        public event Action<Vector2> Moved;
        public event Action Released;

        private RectTransform Rect => (RectTransform)transform;

        private Vector2 ToLocal(PointerEventData eventData)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, eventData.position, eventData.pressEventCamera, out Vector2 local);
            return local - Rect.rect.center;
        }

        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            // Every pixel counts for a gesture; no drag threshold.
            eventData.useDragThreshold = false;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if(eventData.button == PointerEventData.InputButton.Left) Pressed?.Invoke(ToLocal(eventData));
        }

        public void OnDrag(PointerEventData eventData)
        {
            if(eventData.button == PointerEventData.InputButton.Left) Moved?.Invoke(ToLocal(eventData));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if(eventData.button == PointerEventData.InputButton.Left) Released?.Invoke();
        }
    }

    public enum GestureGuide
    {
        None,
        UpAndDown,
        LeftToRight,
        HalfCircleDown,
        SideToSide,
    }

    /// <summary>
    /// Draws what to do on a <see cref="GesturePad"/>: an optional ingredient disc, a guide shape for
    /// the current gesture, and a fading trail of the pointer.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class GesturePadGraphic : MaskableGraphic
    {
        private const float TrailSeconds = 0.35f;

        private readonly List<(Vector2 point, float time, int stroke)> trail = new List<(Vector2, float, int)>();
        private int strokeId;
        private GestureGuide guide;
        private Color guideColor = new Color(1f, 1f, 1f, 0.35f);
        private bool showDisc;
        private Color discColor;
        private Color trailColor = new Color(1f, 0.8f, 0.5f, 1f);

        private Vector2 Center => rectTransform.rect.center;
        private float Size => Mathf.Min(rectTransform.rect.width, rectTransform.rect.height);

        public void SetGuide(GestureGuide newGuide, Color color)
        {
            guide = newGuide;
            guideColor = color;
            SetVerticesDirty();
        }

        public void SetDisc(bool show, Color color)
        {
            showDisc = show;
            discColor = color;
            SetVerticesDirty();
        }

        public void SetTrailColor(Color color)
        {
            trailColor = color;
        }

        /// <summary>Adds a trail point, in the pad's centred local space. A new stroke isn't joined to the last one.</summary>
        public void AddTrailPoint(Vector2 centred, bool newStroke = false)
        {
            if(newStroke) strokeId++;
            trail.Add((Center + centred, Time.unscaledTime, strokeId));
            SetVerticesDirty();
        }

        private void Update()
        {
            if(trail.Count == 0) return;

            float cutoff = Time.unscaledTime - TrailSeconds;
            int removed = trail.RemoveAll(entry => entry.time < cutoff);
            if(removed > 0 || trail.Count > 0) SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 c = Center;
            float size = Size;

            if(showDisc) Disc(vh, c, size * 0.16f, discColor, 28);

            switch(guide)
            {
                case GestureGuide.UpAndDown:
                {
                    Vector2 top = c + Vector2.up * size * 0.38f;
                    Vector2 bottom = c + Vector2.down * size * 0.38f;
                    Dashed(vh, top, bottom, 3f, guideColor);
                    Arrow(vh, top, Vector2.up, guideColor);
                    Arrow(vh, bottom, Vector2.down, guideColor);
                    break;
                }
                case GestureGuide.LeftToRight:
                {
                    float half = rectTransform.rect.width * 0.36f;
                    Vector2 left = c + Vector2.left * half;
                    Vector2 right = c + Vector2.right * half;
                    Dashed(vh, left, right, 3f, guideColor);
                    Arrow(vh, right, Vector2.right, guideColor);
                    break;
                }
                case GestureGuide.SideToSide:
                {
                    float half = rectTransform.rect.width * 0.36f;
                    Vector2 left = c + Vector2.left * half;
                    Vector2 right = c + Vector2.right * half;
                    Dashed(vh, left, right, 3f, guideColor);
                    Arrow(vh, right, Vector2.right, guideColor);
                    Arrow(vh, left, Vector2.left, guideColor);
                    break;
                }
                case GestureGuide.HalfCircleDown:
                {
                    float radius = size * 0.36f;
                    Color faint = guideColor;
                    faint.a *= 0.35f;
                    Arc(vh, c, radius, 90f, 270f, faint);
                    Arc(vh, c, radius, 90f, -90f, guideColor);
                    Vector2 end = c + new Vector2(0f, -radius);
                    Arrow(vh, end, Vector2.left, guideColor);
                    Disc(vh, c + new Vector2(0f, radius), 6f, guideColor, 12);
                    break;
                }
            }

            for(int i = 1; i < trail.Count; i++)
            {
                if(trail[i].stroke != trail[i - 1].stroke) continue;
                float age = (Time.unscaledTime - trail[i].time) / TrailSeconds;
                Color color = trailColor;
                color.a = Mathf.Clamp01(1f - age);
                Line(vh, trail[i - 1].point, trail[i].point, 5f * (1f - age * 0.6f), color);
            }
        }

        // ------------------------------------------------------------------ Mesh helpers

        private static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color)
        {
            Vector2 dir = b - a;
            if(dir.sqrMagnitude < 0.0001f) return;
            Vector2 n = new Vector2(-dir.y, dir.x).normalized * (width * 0.5f);
            int start = vh.currentVertCount;
            vh.AddVert(a - n, color, Vector2.zero);
            vh.AddVert(a + n, color, Vector2.zero);
            vh.AddVert(b + n, color, Vector2.zero);
            vh.AddVert(b - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }

        private static void Dashed(VertexHelper vh, Vector2 a, Vector2 b, float width, Color color, float dash = 10f, float gap = 7f)
        {
            float length = Vector2.Distance(a, b);
            if(length < 0.01f) return;
            Vector2 dir = (b - a) / length;
            for(float d = 0f; d < length; d += dash + gap) Line(vh, a + dir * d, a + dir * Mathf.Min(d + dash, length), width, color);
        }

        private static void Arrow(VertexHelper vh, Vector2 tip, Vector2 direction, Color color, float length = 16f, float width = 10f)
        {
            Vector2 d = direction.normalized;
            Vector2 n = new Vector2(-d.y, d.x) * width;
            Vector2 back = tip - d * length;
            int start = vh.currentVertCount;
            vh.AddVert(tip, color, Vector2.zero);
            vh.AddVert(back + n, color, Vector2.zero);
            vh.AddVert(back - n, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
        }

        /// <summary>Arc from <paramref name="fromDegrees"/> to <paramref name="toDegrees"/> (0 = right, 90 = up).</summary>
        private static void Arc(VertexHelper vh, Vector2 center, float radius, float fromDegrees, float toDegrees, Color color, int segments = 32)
        {
            Vector2 previous = Polar(center, radius, fromDegrees);

            for(int i = 1; i <= segments; i++)
            {
                Vector2 next = Polar(center, radius, Mathf.Lerp(fromDegrees, toDegrees, (float)i / segments));
                if(i % 2 == 1) Line(vh, previous, next, 3f, color);
                previous = next;
            }
        }

        private static Vector2 Polar(Vector2 center, float radius, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Cos(r), Mathf.Sin(r)) * radius;
        }

        private static void Disc(VertexHelper vh, Vector2 center, float radius, Color color, int segments)
        {
            int start = vh.currentVertCount;
            vh.AddVert(center, color, Vector2.zero);

            for(int i = 0; i <= segments; i++)
            {
                vh.AddVert(Polar(center, radius, i * 360f / segments), color, Vector2.zero);
                if(i > 0) vh.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
