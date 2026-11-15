using System.Collections.Generic;

namespace Cooking.Prototype
{
    /// <summary>
    /// What you've picked in the store but not paid for yet. Add ingredients one by one or by recipe,
    /// then <see cref="Checkout"/> pays for everything and moves it into stock raw.
    /// </summary>
    public class ShoppingCart
    {
        public struct Line
        {
            public IngredientDefinition Ingredient;
            public int Quantity;
            public float Cost => Ingredient.Price * Quantity;
        }

        // Insertion order, so the cart reads in the order you picked things.
        private readonly List<IngredientDefinition> order = new List<IngredientDefinition>();
        private readonly Dictionary<IngredientDefinition, int> counts = new Dictionary<IngredientDefinition, int>();

        public bool IsEmpty => counts.Count == 0;

        public int Count(IngredientDefinition ingredient)
        {
            return ingredient != null && counts.TryGetValue(ingredient, out int count) ? count : 0;
        }

        public int TotalUnits
        {
            get
            {
                int total = 0;
                foreach(int count in counts.Values) total += count;
                return total;
            }
        }

        public float TotalCost
        {
            get
            {
                float total = 0f;
                foreach(KeyValuePair<IngredientDefinition, int> pair in counts) total += pair.Key.Price * pair.Value;
                return total;
            }
        }

        public List<Line> Lines()
        {
            List<Line> lines = new List<Line>(order.Count);
            foreach(IngredientDefinition ingredient in order) lines.Add(new Line { Ingredient = ingredient, Quantity = counts[ingredient] });
            return lines;
        }

        public void Add(IngredientDefinition ingredient, int quantity = 1)
        {
            if(ingredient == null || quantity <= 0) return;

            if(!counts.TryGetValue(ingredient, out int existing)) order.Add(ingredient);
            counts[ingredient] = existing + quantity;
        }

        public void Remove(IngredientDefinition ingredient, int quantity = 1)
        {
            int existing = Count(ingredient);
            if(existing <= 0 || quantity <= 0) return;

            if(existing <= quantity)
            {
                counts.Remove(ingredient);
                order.Remove(ingredient);
            }
            else
            {
                counts[ingredient] = existing - quantity;
            }
        }

        public void Clear()
        {
            counts.Clear();
            order.Clear();
        }

        /// <summary>Units of each ingredient one serving of <paramref name="recipe"/> needs, whatever the prep.</summary>
        public static Dictionary<IngredientDefinition, int> Needs(RecipeDefinition recipe)
        {
            Dictionary<IngredientDefinition, int> needs = new Dictionary<IngredientDefinition, int>();
            if(recipe == null) return needs;

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;
                needs.TryGetValue(portion.ingredient, out int existing);
                needs[portion.ingredient] = existing + portion.quantity;
            }

            return needs;
        }

        /// <summary>Adds everything for <paramref name="servings"/> of <paramref name="recipe"/>. Returns units added.</summary>
        public int AddRecipe(RecipeDefinition recipe, int servings = 1)
        {
            int added = 0;
            if(recipe == null) return added;

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;
                Add(portion.ingredient, portion.quantity * servings);
                added += portion.quantity * servings;
            }

            return added;
        }

        /// <summary>
        /// Adds only what one more serving of <paramref name="recipe"/> still lacks, counting what's in
        /// stock (any prep) and already in the cart. Returns units added; 0 means you're covered.
        /// </summary>
        public int AddMissing(RecipeDefinition recipe, Inventory stock)
        {
            int added = 0;

            foreach(KeyValuePair<IngredientDefinition, int> need in Needs(recipe))
            {
                int have = (stock != null ? stock.Total(need.Key) : 0) + Count(need.Key);
                int missing = need.Value - have;
                if(missing <= 0) continue;

                Add(need.Key, missing);
                added += missing;
            }

            return added;
        }

        /// <summary>
        /// Pays for the whole cart and moves it into <paramref name="stock"/> raw. Fails without
        /// changing anything when <paramref name="money"/> doesn't cover it.
        /// </summary>
        public bool Checkout(Inventory stock, ref float money)
        {
            float total = TotalCost;
            if(IsEmpty || total > money + 0.0001f) return false;

            foreach(KeyValuePair<IngredientDefinition, int> pair in counts) stock.Add(pair.Key, PrepSteps.None, pair.Value);
            money -= total;
            Clear();
            return true;
        }
    }
}
