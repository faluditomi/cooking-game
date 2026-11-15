using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>How close a served dish is to the ordered recipe, from 0 (nothing like it) to 1 (the same).</summary>
    public struct Similarity
    {
        /// <summary>How alike they taste: 1 at the same point on the radar, 0 at <see cref="DishSimilarity.Settings.FlavourRange"/> apart or more.</summary>
        public float Flavour;
        /// <summary>How much of the recipe went in as written. Right ingredient with wrong prep/cooking/doneness counts half.</summary>
        public float Ingredients;
        /// <summary>Flavour and ingredients blended by <see cref="DishSimilarity.Settings.FlavourWeight"/>.</summary>
        public float Combined;
    }

    /// <summary>
    /// Scores a plate against the recipe it was meant to be, combining taste (radar distance) with
    /// ingredients (what went in, and how it was prepped and cooked).
    /// </summary>
    public static class DishSimilarity
    {
        public struct Settings
        {
            /// <summary>Share of the combined score that comes from flavour; the rest is ingredients.</summary>
            public float FlavourWeight;
            /// <summary>Radar distance at which flavour similarity reaches 0. The radar runs 0–10 on 8 axes.</summary>
            public float FlavourRange;

            public static Settings Default => new Settings { FlavourWeight = 0.5f, FlavourRange = 6f };
        }

        public static Similarity Compare(RecipeDefinition order, IReadOnlyList<IngredientPortion> served, FlavourRules rules, Settings settings)
        {
            if(order == null) return new Similarity();

            float range = Mathf.Max(0.01f, settings.FlavourRange);
            FlavourProfile ordered = order.CalculateProfile(rules);
            FlavourProfile dish = FlavourCalculator.Calculate(served, rules);

            Similarity similarity = new Similarity
            {
                Flavour = HasFood(served) ? Mathf.Clamp01(1f - FlavourProfile.Distance(ordered, dish) / range) : 0f,
                Ingredients = IngredientMatch(order.Portions, served),
            };

            float weight = Mathf.Clamp01(settings.FlavourWeight);
            similarity.Combined = similarity.Flavour * weight + similarity.Ingredients * (1f - weight);
            return similarity;
        }

        /// <summary>
        /// Matched units over the larger of ordered and served units. A unit with the right ingredient,
        /// prep and cooking (and cooked well) is a full match; the right ingredient otherwise is half.
        /// Extra or missing units both pull the score down.
        /// </summary>
        public static float IngredientMatch(IReadOnlyList<IngredientPortion> ordered, IReadOnlyList<IngredientPortion> served)
        {
            Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> wanted = new Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int>();
            Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> exact = new Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int>();
            Dictionary<IngredientDefinition, int> wantedLeft = new Dictionary<IngredientDefinition, int>();
            Dictionary<IngredientDefinition, int> servedLeft = new Dictionary<IngredientDefinition, int>();
            int orderedUnits = 0;
            int servedUnits = 0;

            foreach(IngredientPortion portion in Valid(ordered))
            {
                Add(wanted, (portion.ingredient, portion.prep, portion.cooking), portion.quantity);
                orderedUnits += portion.quantity;
            }

            foreach(IngredientPortion portion in Valid(served))
            {
                servedUnits += portion.quantity;
                bool cookedWell = portion.IsRaw || portion.doneness == Doneness.Perfect;

                if(cookedWell) Add(exact, (portion.ingredient, portion.prep, portion.cooking), portion.quantity);
                else Add(servedLeft, portion.ingredient, portion.quantity);
            }

            if(orderedUnits == 0 || servedUnits == 0) return 0f;

            float matched = 0f;

            // Full matches first; whatever is left over on either side may still be the right ingredient.
            foreach(KeyValuePair<(IngredientDefinition, PrepSteps, CookMethods), int> want in wanted)
            {
                exact.TryGetValue(want.Key, out int got);
                int full = Mathf.Min(want.Value, got);
                matched += full;
                Add(wantedLeft, want.Key.Item1, want.Value - full);
            }

            foreach(KeyValuePair<(IngredientDefinition, PrepSteps, CookMethods), int> got in exact)
            {
                wanted.TryGetValue(got.Key, out int want);
                Add(servedLeft, got.Key.Item1, got.Value - Mathf.Min(want, got.Value));
            }

            foreach(KeyValuePair<IngredientDefinition, int> left in wantedLeft)
            {
                servedLeft.TryGetValue(left.Key, out int spare);
                matched += 0.5f * Mathf.Min(left.Value, spare);
            }

            return Mathf.Clamp01(matched / Mathf.Max(orderedUnits, servedUnits));
        }

        private static IEnumerable<IngredientPortion> Valid(IReadOnlyList<IngredientPortion> portions)
        {
            if(portions == null) yield break;
            foreach(IngredientPortion portion in portions) if(portion != null && portion.IsValid) yield return portion;
        }

        private static bool HasFood(IReadOnlyList<IngredientPortion> portions)
        {
            foreach(IngredientPortion _ in Valid(portions)) return true;
            return false;
        }

        private static void Add<TKey>(Dictionary<TKey, int> counts, TKey key, int amount)
        {
            if(amount <= 0) return;
            counts.TryGetValue(key, out int existing);
            counts[key] = existing + amount;
        }
    }
}
