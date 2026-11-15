namespace Cooking
{
    /// <summary>
    /// One spoke of the flavour radar chart. Values live on a 0..<see cref="FlavourProfile.MaxValue"/>
    /// scale. Texture runs from mushy (0) to crunchy (max); every other axis runs from none to intense.
    /// </summary>
    // Values are serialized by name inside FlavourProfile, but keep the numbers stable anyway:
    // tools and save data may index by them.
    public enum FlavourAxis
    {
        Savoury = 0,
        Salty = 1,
        Sweet = 2,
        Bitter = 3,
        Sour = 4,
        Pain = 5,
        Nutrition = 6,
        Texture = 7,
    }

    public static class FlavourAxes
    {
        /// <summary>All axes in radar-chart order (clockwise from the top).</summary>
        public static readonly FlavourAxis[] All =
        {
            FlavourAxis.Savoury,
            FlavourAxis.Salty,
            FlavourAxis.Sweet,
            FlavourAxis.Bitter,
            FlavourAxis.Sour,
            FlavourAxis.Pain,
            FlavourAxis.Nutrition,
            FlavourAxis.Texture,
        };

        public const int Count = 8;

        /// <summary>
        /// True for axes that are averaged across a dish (weighted by bulk) instead of accumulated
        /// as a concentration. A dish is not crunchier because it has more crunchy things in it,
        /// it is crunchier because a bigger share of it is crunchy.
        /// </summary>
        public static bool IsAveraged(FlavourAxis axis)
        {
            return axis == FlavourAxis.Texture;
        }

        /// <summary>Short label for charts.</summary>
        public static string Label(FlavourAxis axis)
        {
            switch(axis)
            {
                case FlavourAxis.Savoury: return "Savoury";
                case FlavourAxis.Salty: return "Salty";
                case FlavourAxis.Sweet: return "Sweet";
                case FlavourAxis.Bitter: return "Bitter";
                case FlavourAxis.Sour: return "Sour";
                case FlavourAxis.Pain: return "Pain";
                case FlavourAxis.Nutrition: return "Nutrition";
                case FlavourAxis.Texture: return "Crunch";
                default: return axis.ToString();
            }
        }
    }
}
