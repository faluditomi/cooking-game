using System.Collections.Generic;
using UnityEngine;

namespace Cooking
{
    /// <summary>The best zone for a profile and how well the profile fits it.</summary>
    public readonly struct FlavourZoneMatch
    {
        /// <summary>Matched zone, or null when no zone scored above 0.</summary>
        public readonly FlavourZone Zone;

        /// <summary>1 inside the zone, falling to 0 at the falloff distance.</summary>
        public readonly float Score;

        public FlavourZoneMatch(FlavourZone zone, float score)
        {
            Zone = zone;
            Score = score;
        }

        public bool IsMatch => Zone != null && Score > 0f;

        /// <summary>True when the dish is fully inside the zone.</summary>
        public bool IsInside => Zone != null && Score >= 1f;
    }

    /// <summary>
    /// Judges profiles against <see cref="FlavourZone"/>s. This is where "is this tasty?",
    /// "is this close enough to what they ordered?" and "is this their dream meal?" get answered.
    /// </summary>
    public static class FlavourEvaluator
    {
        /// <summary>
        /// 1 when <paramref name="profile"/> is inside <paramref name="zone"/>, falling linearly to
        /// 0 when it is <paramref name="falloff"/> radar units outside. With falloff 0, only inside counts.
        /// </summary>
        public static float Score(FlavourProfile profile, FlavourZone zone, float falloff = FlavourRules.DefaultZoneFalloff)
        {
            if(zone == null) return 0f;

            // A zone with a shape on the chart is judged by where the dish's dot lands.
            if(zone.HasChartShape) return FlavourDot.Score(FlavourDot.Of(profile), zone);

            float distance = zone.DistanceOutside(profile);

            if(distance <= 0f) return 1f;
            if(falloff <= 0f) return 0f;

            return Mathf.Clamp01(1f - distance / falloff);
        }

        /// <summary>The highest-scoring zone for <paramref name="profile"/>. Ties keep the earlier zone.</summary>
        public static FlavourZoneMatch FindBestZone(FlavourProfile profile, IReadOnlyList<FlavourZone> zones,
            float falloff = FlavourRules.DefaultZoneFalloff)
        {
            FlavourZone best = null;
            float bestScore = 0f;

            if(zones == null) return new FlavourZoneMatch(null, 0f);

            for(int i = 0; i < zones.Count; i++)
            {
                float score = Score(profile, zones[i], falloff);

                if(score > bestScore)
                {
                    best = zones[i];
                    bestScore = score;
                }
            }

            return new FlavourZoneMatch(best, bestScore);
        }

        /// <summary>
        /// The zone both profiles fit best, scored by the weaker of the two fits. Use it to decide
        /// whether a served dish is an acceptable stand-in for the ordered one: same zone, same appeal.
        /// </summary>
        public static FlavourZoneMatch FindSharedZone(FlavourProfile a, FlavourProfile b,
            IReadOnlyList<FlavourZone> zones, float falloff = FlavourRules.DefaultZoneFalloff)
        {
            FlavourZone best = null;
            float bestScore = 0f;

            if(zones == null) return new FlavourZoneMatch(null, 0f);

            for(int i = 0; i < zones.Count; i++)
            {
                float score = Mathf.Min(Score(a, zones[i], falloff), Score(b, zones[i], falloff));

                if(score > bestScore)
                {
                    best = zones[i];
                    bestScore = score;
                }
            }

            return new FlavourZoneMatch(best, bestScore);
        }
    }
}
