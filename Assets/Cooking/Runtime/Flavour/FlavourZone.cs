using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// A region of the flavour radar: a min/max band per axis. Shown to the player as a shaded
    /// area, and used to decide whether a dish is good, whether it can stand in for the ordered
    /// dish, and whether it hits an NPC's secret desire.
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

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Color Color => color;
        public string Description => description;
        public FlavourProfile Min => min;
        public FlavourProfile Max => max;

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

        /// <summary>Sets the data in code. Intended for tests and editor tooling.</summary>
        public void Configure(string newDisplayName, FlavourProfile newMin, FlavourProfile newMax)
        {
            displayName = newDisplayName;
            min = newMin;
            max = newMax;
        }
    }
}
