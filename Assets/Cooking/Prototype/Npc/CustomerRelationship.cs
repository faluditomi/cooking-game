using System.Collections.Generic;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>How far an NPC has opened up to Emphis (Miro board: Stranger → Familiar → Friendly).</summary>
    public enum NpcStage
    {
        /// <summary>Where everyone starts. Short, rude, no thank-yous.</summary>
        Stranger = 0,
        /// <summary>After a few liked meals. They chat while you cook and drop clues about what they crave.</summary>
        Familiar = 1,
        /// <summary>They got their dream meal. Friends now; they often order it.</summary>
        Friendly = 2,
    }

    /// <summary>What a served dish changed for a customer.</summary>
    public struct RelationshipChange
    {
        public NpcStage Before;
        public NpcStage After;
        /// <summary>A dish they liked without ordering, now added to what they order. Null if none.</summary>
        public RecipeDefinition NewLikedMeal;
        /// <summary>True when this serve was their dream meal for the first time.</summary>
        public bool FoundDream;
        /// <summary>
        /// The first good-tasting dish for someone whose first liked meals aren't really food
        /// (<see cref="Customer.firstTasteReplacesLikes"/>): it replaced them all.
        /// </summary>
        public bool FirstTaste;

        public bool StageChanged => Before != After;
    }

    /// <summary>
    /// One regular's progress with the stall, kept for the session. Tracks their stage, how many
    /// meals they've liked, the dishes they like (and order), and their dream meal once found.
    /// </summary>
    /// <remarks>
    /// Dishes the player improvises are stored as runtime <see cref="RecipeDefinition"/>s named
    /// <see cref="Nameless"/> (origin "Emphis") until the player saves them with a name, so they can
    /// be ordered back and matched exactly like menu recipes.
    /// </remarks>
    public class CustomerRelationship
    {
        /// <summary>The name of a dish the player hasn't named.</summary>
        public const string Nameless = "Nameless";

        public readonly Customer Customer;

        public NpcStage Stage { get; private set; } = NpcStage.Stranger;
        /// <summary>Meals they liked: their exact order, very close to it, or their dream meal.</summary>
        public int LikedCount { get; private set; }
        public RecipeDefinition DreamMeal { get; private set; }
        /// <summary>For <see cref="Customer.firstTasteReplacesLikes"/>: they've had a dish that tastes good, and their first liked meals are gone.</summary>
        public bool HasTasted { get; private set; }

        /// <summary>Closest any served dish has come to their secret desire (radar distance). Infinity until the first.</summary>
        public float BestDesireDistance { get; private set; } = float.PositiveInfinity;

        /// <summary>Compares a dish's distance to their desire with the best so far, then remembers it.</summary>
        public Warmth RecordDesireDistance(float distance)
        {
            Warmth warmth = DesireClue.Compare(distance, BestDesireDistance);
            if(distance < BestDesireDistance) BestDesireDistance = distance;
            return warmth;
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
        /// What they order by default: their dream meal half the time once found, otherwise one of
        /// their liked meals, or anything on <paramref name="menu"/> if they have none. Their order
        /// dialogue can name a specific dish instead.
        /// </summary>
        public RecipeDefinition PickOrder(System.Random random, IReadOnlyList<RecipeDefinition> menu)
        {
            if(DreamMeal != null && (likedMeals.Count == 0 || random.Next(2) == 0)) return DreamMeal;
            return PickLiked(random) ?? (menu != null && menu.Count > 0 ? menu[random.Next(menu.Count)] : null);
        }

        /// <summary>One of their liked meals at random, or null if they like nothing yet.</summary>
        public RecipeDefinition PickLiked(System.Random random)
        {
            return likedMeals.Count > 0 ? likedMeals[random.Next(likedMeals.Count)] : null;
        }

        /// <summary>
        /// Updates the relationship after serving <paramref name="served"/>. A liked dish that matches
        /// one of <paramref name="known"/> exactly is remembered as that recipe; otherwise a
        /// <see cref="Nameless"/> recipe is made from the plate.
        /// </summary>
        public RelationshipChange Record(JudgeResult result, IReadOnlyList<IngredientPortion> served, IReadOnlyList<RecipeDefinition> known = null)
        {
            RelationshipChange change = new RelationshipChange { Before = Stage };

            if(result.Verdict == Verdict.DreamMeal)
            {
                LikedCount++;

                if(DreamMeal == null)
                {
                    DreamMeal = FindMatch(known, served) ?? CreateMeal(Nameless, served);
                    change.FoundDream = true;
                }

                // Real food at last: their first liked meals go.
                if(Customer.firstTasteReplacesLikes && !HasTasted)
                {
                    HasTasted = true;
                    likedMeals.Clear();
                }

                Stage = NpcStage.Friendly;
            }
            else if(Customer.firstTasteReplacesLikes && !HasTasted)
            {
                // Their first liked meals (a raw potato, an MSG2 cube...) don't count towards
                // anything. The first dish that actually tastes good replaces them all, and that one
                // dish makes them Familiar.
                bool firstLike = FindMatch(Customer.orders, served) != null;

                if(!firstLike && result.Verdict != Verdict.Refused && TastesGood(result))
                {
                    HasTasted = true;
                    LikedCount++;
                    likedMeals.Clear();
                    change.NewLikedMeal = FindMatch(known, served) ?? CreateMeal(Nameless, served);
                    likedMeals.Add(change.NewLikedMeal);
                    change.FirstTaste = true;
                    if(Stage == NpcStage.Stranger) Stage = NpcStage.Familiar;
                }
            }
            else if(result.IsLiked)
            {
                LikedCount++;

                // Very close but not the order: they liked it, so they'll ask for it again.
                if(result.Verdict == Verdict.VeryClose && FindMatch(likedMeals, served) == null)
                {
                    change.NewLikedMeal = FindMatch(known, served) ?? CreateMeal(Nameless, served);
                    likedMeals.Add(change.NewLikedMeal);
                }

                if(Stage == NpcStage.Stranger && LikedCount >= Mathf.Max(1, Customer.likedMealsToFamiliar)) Stage = NpcStage.Familiar;
            }

            change.After = Stage;
            return change;
        }

        /// <summary>A dish inside one of the desirable flavour zones.</summary>
        public static bool TastesGood(JudgeResult result)
        {
            return result.BestZone.Zone != null && result.BestZone.Score >= 1f;
        }

        /// <summary>The first of <paramref name="recipes"/> the plate makes exactly, or null.</summary>
        public static RecipeDefinition FindMatch(IReadOnlyList<RecipeDefinition> recipes, IReadOnlyList<IngredientPortion> served)
        {
            if(recipes == null) return null;
            List<IngredientPortion> perfect = Perfect(served);
            if(perfect.Count == 0) return null;

            foreach(RecipeDefinition recipe in recipes)
            {
                if(recipe != null && recipe.Matches(perfect)) return recipe;
            }

            return null;
        }

        /// <summary>A recipe made from what was on the plate, cooked perfectly, credited to Emphis.</summary>
        public static RecipeDefinition CreateMeal(string displayName, IReadOnlyList<IngredientPortion> served)
        {
            RecipeDefinition recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.name = displayName;
            recipe.hideFlags = HideFlags.DontSave;
            recipe.Configure(displayName, "Emphis", Perfect(served));
            return recipe;
        }

        /// <summary>Names a dish (the player saving it to the menu). Blank names are ignored.</summary>
        public static void Rename(RecipeDefinition recipe, string displayName)
        {
            if(recipe == null || string.IsNullOrWhiteSpace(displayName)) return;

            List<IngredientPortion> portions = new List<IngredientPortion>(recipe.Portions);
            recipe.name = displayName.Trim();
            recipe.Configure(displayName.Trim(), recipe.Origin, portions);
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
