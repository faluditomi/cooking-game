using System.Collections.Generic;

namespace Cooking.Prototype
{
    /// <summary>
    /// The stall's stock: how many units of each ingredient you have, per prep state. Bought stock
    /// is raw; prepping a unit moves it to a prepped entry. Prepped stock spoils overnight.
    /// </summary>
    public class Inventory
    {
        public struct Entry
        {
            public IngredientDefinition Ingredient;
            public PrepSteps Prep;
            public int Quantity;
        }

        private readonly Dictionary<(IngredientDefinition, PrepSteps), int> counts = new Dictionary<(IngredientDefinition, PrepSteps), int>();

        public bool IsEmpty => counts.Count == 0;

        public int Count(IngredientDefinition ingredient, PrepSteps prep)
        {
            return ingredient != null && counts.TryGetValue((ingredient, prep), out int count) ? count : 0;
        }

        /// <summary>All units of <paramref name="ingredient"/>, whatever their prep.</summary>
        public int Total(IngredientDefinition ingredient)
        {
            int total = 0;

            foreach(KeyValuePair<(IngredientDefinition, PrepSteps), int> pair in counts)
            {
                if(pair.Key.Item1 == ingredient) total += pair.Value;
            }

            return total;
        }

        public void Add(IngredientDefinition ingredient, PrepSteps prep, int quantity = 1)
        {
            if(ingredient == null || quantity <= 0) return;

            counts.TryGetValue((ingredient, prep), out int existing);
            counts[(ingredient, prep)] = existing + quantity;
        }

        /// <summary>Removes units. Fails without changing anything when there aren't enough.</summary>
        public bool Take(IngredientDefinition ingredient, PrepSteps prep, int quantity = 1)
        {
            int existing = Count(ingredient, prep);
            if(quantity <= 0 || existing < quantity) return false;

            if(existing == quantity) counts.Remove((ingredient, prep));
            else counts[(ingredient, prep)] = existing - quantity;

            return true;
        }

        /// <summary>Entries sorted by ingredient name, raw first, then by prep.</summary>
        public List<Entry> Entries()
        {
            List<Entry> entries = new List<Entry>(counts.Count);

            foreach(KeyValuePair<(IngredientDefinition, PrepSteps), int> pair in counts)
            {
                entries.Add(new Entry { Ingredient = pair.Key.Item1, Prep = pair.Key.Item2, Quantity = pair.Value });
            }

            entries.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.Ingredient.DisplayName, b.Ingredient.DisplayName);
                return byName != 0 ? byName : ((int)a.Prep).CompareTo((int)b.Prep);
            });

            return entries;
        }

        /// <summary>Removes every prepped unit (end of day) and returns what was lost.</summary>
        public List<Entry> SpoilPrepped()
        {
            List<Entry> spoiled = new List<Entry>();

            foreach(Entry entry in Entries())
            {
                if(entry.Prep == PrepSteps.None) continue;
                spoiled.Add(entry);
                counts.Remove((entry.Ingredient, entry.Prep));
            }

            return spoiled;
        }

        public void Clear()
        {
            counts.Clear();
        }
    }
}
