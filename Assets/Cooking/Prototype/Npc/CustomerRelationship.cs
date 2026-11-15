using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>How far an NPC has opened up to Emphis (design doc: Conversation → Evolution of an NPC).</summary>
    public enum NpcStage
    {
        /// <summary>Where everyone starts. Short, gloomy, no thank-yous.</summary>
        Dejected = 0,
        /// <summary>After a few happy meals. They start dropping clues about what they crave.</summary>
        WarmedUp = 1,
        /// <summary>They got their dream meal. They always order it from now on.</summary>
        Eureka = 2,
    }

    /// <summary>What a served dish changed for a customer.</summary>
    public struct RelationshipChange
    {
        public NpcStage Before;
        public NpcStage After;
        /// <summary>A dish they liked without ordering, now added to what they order. Null if none.</summary>
        public RecipeDefinition NewLikedMeal;

        public bool StageChanged => Before != After;
    }

    /// <summary>
    /// One regular's progress with the stall, kept for the session. Tracks their stage, how many
    /// happy meals they've had, the dishes they like (and order), and their dream meal once found.
    /// </summary>
    /// <remarks>
    /// Dishes the player improvises are stored as runtime <see cref="RecipeDefinition"/>s (origin
    /// "Emphis"), so they can be ordered back and matched exactly like menu recipes.
    /// </remarks>
    public class CustomerRelationship
    {
        public readonly Customer Customer;

        public NpcStage Stage { get; private set; } = NpcStage.Dejected;
        public int HappyMeals { get; private set; }
        public RecipeDefinition DreamMeal { get; private set; }

        /// <summary>Closest any served dish has come to their secret desire (radar distance). Infinity until the first.</summary>
        public float BestDesireDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Compares a dish's distance to their desire with the best so far, then remembers it.</summary>
        public Warmth RecordDesireDistance(float distance)
        {
            Warmth warmth = DesireClue.Compare(distance, BestDesireDistance);
            if(distance < BestDesireDistance) BestDesireDistance = distance;
            return warmth;
        }

        /// <summary>Renames the discovered dream meal (the player names it for the recipe book).</summary>
        public void NameDreamMeal(string displayName)
        {
            if(DreamMeal == null || string.IsNullOrWhiteSpace(displayName)) return;

            List<IngredientPortion> portions = new List<IngredientPortion>(DreamMeal.Portions);
            DreamMeal.name = displayName.Trim();
            DreamMeal.Configure(displayName.Trim(), DreamMeal.Origin, portions);
        }

        private readonly List<RecipeDefinition> likedMeals = new List<RecipeDefinition>();
        public IReadOnlyList<RecipeDefinition> LikedMeals => likedMeals;

        public CustomerRelationship(Customer customer)
        {
            Customer = customer;

            if(customer?.orders != null)
            {
                foreach(RecipeDefinition recipe in customer.orders)
                {
                    if(recipe != null && !likedMeals.Contains(recipe)) likedMeals.Add(recipe);
                }
            }
        }

        /// <summary>
        /// What they order: their dream meal once found, otherwise one of their liked meals, or
        /// anything on <paramref name="menu"/> if they have none.
        /// </summary>
        public RecipeDefinition PickOrder(System.Random random, IReadOnlyList<RecipeDefinition> menu)
        {
            if(DreamMeal != null) return DreamMeal;
            if(likedMeals.Count > 0) return likedMeals[random.Next(likedMeals.Count)];
            return menu != null && menu.Count > 0 ? menu[random.Next(menu.Count)] : null;
        }

        /// <summary>Updates the relationship after serving <paramref name="served"/>.</summary>
        public RelationshipChange Record(JudgeResult result, IReadOnlyList<IngredientPortion> served)
        {
            RelationshipChange change = new RelationshipChange { Before = Stage };

            if(result.Verdict == Verdict.DreamMeal)
            {
                // The dream meal is about the food; even served late, it's the eureka moment.
                DreamMeal = CreateMeal(Customer.name + "'s dream meal", served, "Discovered for " + Customer.name);
                Stage = NpcStage.Eureka;
            }
            else if(result.IsHappy)
            {
                HappyMeals++;

                // Very close but not the order: they liked it, so they'll ask for it again.
                if(result.Verdict == Verdict.VeryClose && !AlreadyLiked(served))
                {
                    int specials = CountSpecials();
                    change.NewLikedMeal = CreateMeal("Emphis's special for " + Customer.name + (specials > 0 ? " #" + (specials + 1) : ""), served);
                    likedMeals.Add(change.NewLikedMeal);
                }

                if(Stage == NpcStage.Dejected && HappyMeals >= Mathf.Max(1, Customer.happyMealsToWarmUp)) Stage = NpcStage.WarmedUp;
            }

            change.After = Stage;
            return change;
        }

        private bool AlreadyLiked(IReadOnlyList<IngredientPortion> served)
        {
            foreach(RecipeDefinition recipe in likedMeals)
            {
                if(recipe.Matches(Perfect(served))) return true;
            }

            return false;
        }

        private int CountSpecials()
        {
            int count = 0;
            foreach(RecipeDefinition recipe in likedMeals) if(recipe.Origin == "Emphis") count++;
            return count;
        }

        /// <summary>A recipe made from what was on the plate, cooked perfectly. Credited to Emphis unless said otherwise.</summary>
        private static RecipeDefinition CreateMeal(string displayName, IReadOnlyList<IngredientPortion> served, string origin = "Emphis")
        {
            RecipeDefinition recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.name = displayName;
            recipe.hideFlags = HideFlags.DontSave;
            recipe.Configure(displayName, origin, Perfect(served));
            return recipe;
        }

        private static List<IngredientPortion> Perfect(IReadOnlyList<IngredientPortion> served)
        {
            List<IngredientPortion> portions = new List<IngredientPortion>();
            if(served == null) return portions;

            foreach(IngredientPortion portion in served)
            {
                if(portion == null || !portion.IsValid) continue;
                portions.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, Doneness.Perfect));
            }

            return portions;
        }
    }
}
