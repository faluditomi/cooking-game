using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>Whether a dish got nearer to an NPC's secret desire than anything before it.</summary>
    public enum Warmth
    {
        /// <summary>The first dish they've had, so nothing to compare with.</summary>
        First,
        Warmer,
        Same,
        Colder,
    }

    /// <summary>
    /// What a warmed-up regular lets slip after a dish: whether it's closer to the thing they
    /// crave, and the one flavour that's most off. The clue never names the dish; it nudges.
    /// </summary>
    public static class DesireClue
    {
        /// <summary>Distance change that counts as warmer or colder rather than "same".</summary>
        public const float Threshold = 0.25f;

        public static Warmth Compare(float distance, float bestSoFar)
        {
            if(float.IsInfinity(bestSoFar)) return Warmth.First;
            if(distance < bestSoFar - Threshold) return Warmth.Warmer;
            if(distance > bestSoFar + Threshold) return Warmth.Colder;
            return Warmth.Same;
        }

        /// <summary>The axis furthest outside <paramref name="desire"/>, and whether the dish needs more or less of it.</summary>
        public static bool MostOff(FlavourProfile dish, FlavourZone desire, out FlavourAxis axis, out bool needsMore)
        {
            axis = FlavourAxis.Savoury;
            needsMore = false;
            float worst = 0f;

            foreach(FlavourAxis candidate in FlavourAxes.All)
            {
                float value = dish[candidate];
                float lo = Mathf.Min(desire.Min[candidate], desire.Max[candidate]);
                float hi = Mathf.Max(desire.Min[candidate], desire.Max[candidate]);
                float off = value < lo ? lo - value : value > hi ? value - hi : 0f;

                if(off > worst)
                {
                    worst = off;
                    axis = candidate;
                    needsMore = value < lo;
                }
            }

            return worst > 0f;
        }

        /// <summary>
        /// A short, in-character-enough hint: warmth plus the most-off flavour. When the flavour is
        /// right but the dish misses an ingredient their dream needs, they say that instead.
        /// </summary>
        public static string Describe(FlavourProfile dish, FlavourZone desire, Warmth warmth, bool missingNeed = false)
        {
            string lead;

            switch(warmth)
            {
                case Warmth.Warmer: lead = "That's closer to something... "; break;
                case Warmth.Colder: lead = "No. Further from it than before. "; break;
                case Warmth.Same: lead = "Not quite. "; break;
                default: lead = "It's not what I'm after. "; break;
            }

            if(desire == null || !MostOff(dish, desire, out FlavourAxis axis, out bool needsMore))
            {
                return missingNeed ? lead + "It tastes right. But something in it isn't what it should be." : lead.TrimEnd();
            }

            return lead + Hint(axis, needsMore);
        }

        public static string Hint(FlavourAxis axis, bool needsMore)
        {
            switch(axis)
            {
                case FlavourAxis.Savoury: return needsMore ? "It needs more depth." : "Too heavy, too savoury.";
                case FlavourAxis.Salty: return needsMore ? "It wants more salt." : "Too salty.";
                case FlavourAxis.Sweet: return needsMore ? "It should be sweeter." : "Too sweet.";
                case FlavourAxis.Bitter: return needsMore ? "It needs an edge, something bitter." : "Too bitter.";
                case FlavourAxis.Sour: return needsMore ? "It needs something sharp. Sour." : "Too sharp, too sour.";
                case FlavourAxis.Pain: return needsMore ? "Hotter. Much hotter." : "Too hot.";
                case FlavourAxis.Nutrition: return needsMore ? "It should be more filling." : "Too heavy in the stomach.";
                case FlavourAxis.Texture: return needsMore ? "It needs something to bite into." : "Softer. It should be softer.";
                default: return "";
            }
        }
    }
}
