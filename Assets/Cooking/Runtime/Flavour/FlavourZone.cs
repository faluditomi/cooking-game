using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// A region of the flavour radar, used to decide whether a dish is good and whether it hits an
    /// NPC's favourite flavour. With a chart shape (a wedge), a dish is in the zone when its dot
    /// (<see cref="FlavourDot"/>) lands inside it, which is exactly what the player sees. Without
    /// one, it's a min/max band per axis.
    /// </summary>
    /// <remarks>An axis the zone does not care about is left at 0..10.</remarks>
    [CreateAssetMenu(fileName = "FlavourZone", menuName = "Cooking/Flavour Zone")]
    public class FlavourZone : ScriptableObject
    {
        [SerializeField] private string displayName;
        [SerializeField] private Color color = new Color(0.95f, 0.55f, 0.2f, 1f);
        [TextArea] [SerializeField] private string description;

        [Header("Bounds")]
        [SerializeField] private FlavourProfile min = FlavourProfile.Zero;
        [SerializeField] private FlavourProfile max = FlavourProfile.Filled(FlavourProfile.MaxValue);

        [Header("On the chart (where a dish's dot must land; see FlavourDot)")]
        [Tooltip("Which way the zone points, as an axis position: 0 savoury, 1 salty, 2 sweet, 3 bitter, 4 sour, 5 pain, 6 nutrition, 7 crunch. Fractions are between spokes.")]
        [SerializeField] private float chartAxis;
        [Tooltip("How wide it is either side of that direction, in spokes.")]
        [SerializeField, Min(0f)] private float chartSpread;
        [Tooltip("Inner and outer edge, in units of the chart's radius (0 centre, 1 edge). An outer edge of 0 means the zone has no chart shape and is judged by its bounds instead.")]
        [SerializeField, Range(0f, 1f)] private float chartInner;
        [SerializeField, Range(0f, 1f)] private float chartOuter;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Color Color => color;
        public string Description => description;
        public FlavourProfile Min => min;
        public FlavourProfile Max => max;
        /// <summary>True when the zone is a shape on the chart: dishes are judged by where their dot lands.</summary>
        public bool HasChartShape => chartOuter > chartInner;
        public float ChartAxis => chartAxis;
        public float ChartSpread => chartSpread;
        public float ChartInner => chartInner;
        public float ChartOuter => chartOuter;

        /// <summary>True when every axis of <paramref name="profile"/> is inside the band.</summary>
        public bool Contains(FlavourProfile profile)
        {
            return DistanceOutside(profile) <= 0f;
        }

        /// <summary>
        /// Radar distance from <paramref name="profile"/> to the nearest point of the zone.
        /// 0 when inside.
        /// </summary>
        public float DistanceOutside(FlavourProfile profile)
        {
            float sum = 0f;

            foreach(FlavourAxis axis in FlavourAxes.All)
            {
                float value = profile[axis];
                float lo = Mathf.Min(min[axis], max[axis]);
                float hi = Mathf.Max(min[axis], max[axis]);
                float outside = value < lo ? lo - value : value > hi ? value - hi : 0f;
                sum += outside * outside;
            }

            return Mathf.Sqrt(sum);
        }

        /// <summary>Middle of the zone. Handy for aiming NPC orders or drawing a marker.</summary>
        public FlavourProfile Center => (min + max) * 0.5f;

        /// <summary>Gives the zone a shape on the chart (a wedge). Intended for tests and editor tooling.</summary>
        public void ConfigureChartShape(float axis, float spread, float inner, float outer)
        {
            chartAxis = axis;
            chartSpread = spread;
            chartInner = inner;
            chartOuter = outer;
        }

        /// <summary>Sets the data in code. Intended for tests and editor tooling.</summary>
        public void Configure(string newDisplayName, FlavourProfile newMin, FlavourProfile newMax)
        {
            displayName = newDisplayName;
            min = newMin;
            max = newMax;
        }
    }
}
