using System.Collections.Generic;
using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// Turns a list of <see cref="IngredientPortion"/>s into the dish's <see cref="FlavourProfile"/>.
    /// Pure and stateless, so it is safe to call every frame for a live preview.
    /// </summary>
    /// <remarks>
    /// For each valid portion the per-unit profile is
    /// <c>ingredient.Flavour + processing delta (+ ingredient.RawDelta when raw)</c>, floored at 0.
    /// Taste deltas from <see cref="FlavourRules"/> are scaled by the ingredient's bulk, so frying a
    /// pinch of sugar does not add as much savoury as frying a whole steak.
    /// Then, with <c>w = bulk × quantity</c>:
    /// <list type="bullet">
    /// <item>taste axes: <c>Σ(unit × quantity) / Σw</c>, a concentration. Seasonings with low bulk
    /// add a lot of taste without diluting the dish.</item>
    /// <item>averaged axes (texture): <c>Σ(unit × w) / Σw</c>, a bulk-weighted average.</item>
    /// </list>
    /// The result is clamped to 0..10.
    /// </remarks>
    public static class FlavourCalculator
    {
        /// <summary>Profile of one unit of the portion after prep, cooking and doneness.</summary>
        public static FlavourProfile GetUnitProfile(IngredientPortion portion, FlavourRules rules)
        {
            if(portion == null || portion.ingredient == null) return FlavourProfile.Zero;

            IngredientDefinition ingredient = portion.ingredient;
            FlavourProfile unit = ingredient.Flavour;

            if(rules != null)
            {
                FlavourProfile delta = rules.GetProcessingDelta(portion.prep, portion.cooking, portion.doneness);

                foreach(FlavourAxis axis in FlavourAxes.All)
                {
                    // Taste deltas describe a full (bulk 1) unit; a pinch of seasoning gets a pinch
                    // of the effect. Averaged axes are already weighted by bulk during aggregation.
                    float scale = FlavourAxes.IsAveraged(axis) ? 1f : ingredient.Bulk;
                    unit[axis] += delta[axis] * scale;
                }
            }

            if(portion.IsRaw) unit += ingredient.RawDelta;

            // Without the prep it needs (chilli not grated), it gives less taste.
            float strength = ingredient.TasteStrength(portion.prep);

            if(strength < 1f)
            {
                foreach(FlavourAxis axis in FlavourAxes.All)
                {
                    if(!FlavourAxes.IsAveraged(axis)) unit[axis] *= strength;
                }
            }

            // Only the floor is clamped: seasonings may legitimately exceed the scale per unit,
            // because their intensity is spread across the dish's bulk.
            return unit.Clamped(0f, float.MaxValue);
        }

        /// <summary>Flavour profile of a dish made from <paramref name="portions"/>.</summary>
        /// <param name="rules">Processing tuning. Null ignores prep, cooking and doneness.</param>
        public static FlavourProfile Calculate(IReadOnlyList<IngredientPortion> portions, FlavourRules rules)
        {
            if(portions == null) return FlavourProfile.Zero;

            FlavourProfile sums = FlavourProfile.Zero;
            float totalBulk = 0f;

            for(int i = 0; i < portions.Count; i++)
            {
                IngredientPortion portion = portions[i];
                if(portion == null || !portion.IsValid) continue;

                FlavourProfile unit = GetUnitProfile(portion, rules);
                float weight = portion.ingredient.Bulk * portion.quantity;
                totalBulk += weight;

                foreach(FlavourAxis axis in FlavourAxes.All)
                {
                    float factor = FlavourAxes.IsAveraged(axis) ? weight : portion.quantity;
                    sums[axis] += unit[axis] * factor;
                }
            }

            if(totalBulk <= Mathf.Epsilon) return FlavourProfile.Zero;

            return (sums * (1f / totalBulk)).Clamped();
        }

        /// <summary>Total market cost of the ingredients in <paramref name="portions"/>.</summary>
        public static float CalculateIngredientCost(IReadOnlyList<IngredientPortion> portions)
        {
            if(portions == null) return 0f;

            float cost = 0f;

            for(int i = 0; i < portions.Count; i++)
            {
                IngredientPortion portion = portions[i];
                if(portion == null || !portion.IsValid) continue;
                cost += portion.ingredient.Price * portion.quantity;
            }

            return cost;
        }
    }
}
