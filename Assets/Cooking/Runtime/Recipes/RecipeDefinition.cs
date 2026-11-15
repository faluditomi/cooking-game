using System.Collections.Generic;
using UnityEngine;

namespace Cooking
{
    /// <summary>
    /// A dish on the menu: which portions go in and how they are prepped and cooked. Customers order
    /// these by name; the served plate is compared against it with <see cref="Matches"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "Recipe", menuName = "Cooking/Recipe")]
    public class RecipeDefinition : ScriptableObject
    {
        [SerializeField] private string displayName;
        [Tooltip("Who created the recipe, or where it comes from. Shown as \"Origin: ...\".")]
        [SerializeField] private string origin;
        [TextArea] [SerializeField] private string description;

        [Tooltip("What goes in. Doneness is ignored: an ordered dish is always expected to be cooked perfectly.")]
        [SerializeField] private List<IngredientPortion> portions = new List<IngredientPortion>();

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Origin => origin;
        public string Description => description;
        public IReadOnlyList<IngredientPortion> Portions => portions;

        /// <summary>Every cooking station the recipe uses.</summary>
        public CookMethods Methods
        {
            get
            {
                CookMethods methods = CookMethods.None;

                foreach(IngredientPortion portion in portions)
                {
                    if(portion != null && portion.IsValid) methods |= portion.cooking;
                }

                return methods;
            }
        }

        /// <summary>Copies of the portions, all cooked perfectly. This is the dish the customer expects.</summary>
        public List<IngredientPortion> CreatePerfectPortions()
        {
            List<IngredientPortion> result = new List<IngredientPortion>(portions.Count);

            foreach(IngredientPortion portion in portions)
            {
                if(portion == null || !portion.IsValid) continue;
                result.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, Doneness.Perfect));
            }

            return result;
        }

        /// <summary>Flavour of the recipe when it is cooked perfectly.</summary>
        public FlavourProfile CalculateProfile(FlavourRules rules)
        {
            return FlavourCalculator.Calculate(CreatePerfectPortions(), rules);
        }

        public float CalculateIngredientCost()
        {
            return FlavourCalculator.CalculateIngredientCost(portions);
        }

        /// <summary>
        /// True when <paramref name="dish"/> is exactly this recipe: the same ingredients in the same
        /// quantities with the same prep and cooking, and nothing cooked badly. Portions may be split
        /// or merged differently (two "1× salt" equal one "2× salt").
        /// </summary>
        public bool Matches(IReadOnlyList<IngredientPortion> dish)
        {
            Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> expected = Count(portions, out _);
            Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> actual = Count(dish, out bool allPerfect);

            if(!allPerfect || expected.Count == 0 || expected.Count != actual.Count) return false;

            foreach(KeyValuePair<(IngredientDefinition, PrepSteps, CookMethods), int> pair in expected)
            {
                if(!actual.TryGetValue(pair.Key, out int quantity) || quantity != pair.Value) return false;
            }

            return true;
        }

        private static Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> Count(IReadOnlyList<IngredientPortion> list, out bool allPerfect)
        {
            Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int> counts = new Dictionary<(IngredientDefinition, PrepSteps, CookMethods), int>();
            allPerfect = true;

            if(list == null) return counts;

            for(int i = 0; i < list.Count; i++)
            {
                IngredientPortion portion = list[i];
                if(portion == null || !portion.IsValid) continue;

                (IngredientDefinition, PrepSteps, CookMethods) key = (portion.ingredient, portion.prep, portion.cooking);
                counts.TryGetValue(key, out int existing);
                counts[key] = existing + portion.quantity;

                if(!portion.IsRaw && portion.doneness != Doneness.Perfect) allPerfect = false;
            }

            return counts;
        }

        /// <summary>Sets the data in code. Intended for tests and editor tooling.</summary>
        public void Configure(string newDisplayName, string newOrigin, IEnumerable<IngredientPortion> newPortions, string newDescription = null)
        {
            displayName = newDisplayName;
            origin = newOrigin;
            description = newDescription;
            portions = new List<IngredientPortion>(newPortions);
        }
    }
}
