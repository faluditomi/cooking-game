using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// A dish as one point on the flavour radar, and zones as shapes the point is in or out of.
    /// </summary>
    /// <remarks>
    /// The dot is the centre of the dish's shape on the chart (the average of its corners), but only of what stands out: each
    /// flavour minus the dish's own average, since nearly every ingredient adds some nutrition and
    /// crunch and a plain centre would put every dish in the same corner. It's then magnified by
    /// <see cref="Gain"/> so dishes use the whole chart. Positions are in units of the chart's
    /// radius, y up, the first axis (savoury) straight up and the rest clockwise, like the radar.
    /// </remarks>
    public static class FlavourDot
    {
        /// <summary>How much the centre point is magnified so dishes spread over the chart.</summary>
        public const float Gain = 7f;
        /// <summary>The dot stays a little inside the chart's edge.</summary>
        public const float MaxRadius = 0.95f;
        /// <summary>How far outside a zone (in radius units) its score falls to 0.</summary>
        public const float Falloff = 0.12f;

        public static Vector2 Of(FlavourProfile profile)
        {
            int count = FlavourAxes.Count;
            float[] values = new float[count];
            float mean = 0f;

            for(int i = 0; i < count; i++)
            {
                values[i] = Mathf.Clamp(profile[FlavourAxes.All[i]], 0f, FlavourProfile.MaxValue);
                mean += values[i];
            }

            mean /= count;
            for(int i = 0; i < count; i++) values[i] = Mathf.Max(0f, values[i] - mean) / FlavourProfile.MaxValue;

            // The centre of the shape's corners. (An area centroid would ignore a single strong
            // flavour whose neighbours are 0: a spike has no area.)
            Vector2 centre = Vector2.zero;
            for(int i = 0; i < count; i++) centre += Direction(i) * values[i];
            centre /= count;

            return Vector2.ClampMagnitude(centre * Gain, MaxRadius);
        }

        /// <summary>The direction of axis <paramref name="axisPosition"/> (fractions are between spokes).</summary>
        public static Vector2 Direction(float axisPosition)
        {
            float angle = (90f - axisPosition * 360f / FlavourAxes.Count) * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        }

        /// <summary>Which way a point lies, as an axis position 0..count (0 = savoury, clockwise).</summary>
        public static float AxisPosition(Vector2 point)
        {
            float degrees = 90f - Mathf.Atan2(point.y, point.x) * Mathf.Rad2Deg;
            return Mathf.Repeat(degrees / (360f / FlavourAxes.Count), FlavourAxes.Count);
        }

        /// <summary>1 inside the zone's shape, falling to 0 at <see cref="Falloff"/> outside it.</summary>
        public static float Score(Vector2 dot, FlavourZone zone)
        {
            if(zone == null || !zone.HasChartShape) return 0f;
            float outside = DistanceOutside(dot, zone);
            return outside <= 0f ? 1f : Mathf.Clamp01(1f - outside / Falloff);
        }

        /// <summary>How far (in radius units) the point is outside the zone's wedge; 0 inside.</summary>
        public static float DistanceOutside(Vector2 dot, FlavourZone zone)
        {
            int count = FlavourAxes.Count;
            float radius = dot.magnitude;
            float position = AxisPosition(dot);
            // Angular offset from the wedge's middle, wrapped to -count/2..count/2.
            float offset = Mathf.Repeat(position - zone.ChartAxis + count * 0.5f, count) - count * 0.5f;
            float angularOutside = Mathf.Max(0f, Mathf.Abs(offset) - zone.ChartSpread);
            // Turn it into a distance along the arc at that radius.
            float arc = angularOutside * (Mathf.PI * 2f / count) * Mathf.Max(radius, 0.05f);
            float radialOutside = radius < zone.ChartInner ? zone.ChartInner - radius : radius > zone.ChartOuter ? radius - zone.ChartOuter : 0f;
            return Mathf.Sqrt(arc * arc + radialOutside * radialOutside);
        }
    }
}
