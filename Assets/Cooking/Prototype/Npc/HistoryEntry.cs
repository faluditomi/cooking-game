using System.Collections.Generic;

namespace Cooking.Prototype
{
    /// <summary>Something that happened during a visit, stamped with seconds since the customer arrived.</summary>
    public struct VisitEvent
    {
        public float Time;
        /// <summary>For speech: Order, Hurrying, Reaction or Clue. For cooking: the station or area.</summary>
        public string Tag;
        public string Text;
    }

    /// <summary>
    /// One customer visit in the cooking history (design doc: Recipe App → Cooking history), from
    /// arriving to being served: who it was for, what they ordered, everything said, everything
    /// cooked, what was served, its flavour, and how it went.
    /// </summary>
    public class HistoryEntry
    {
        public int Day;
        /// <summary>Which customer of the day (1-based).</summary>
        public int Visit;
        public Customer Customer;
        public NpcStage Stage;

        // Order
        public RecipeDefinition Order;
        /// <summary>The ordered dish's name when the order came in.</summary>
        public string Ordered;
        public string OrderLine;
        public FlavourProfile OrderedFlavour;
        /// <summary>The order was in your recipe book. If not, its flavour stays hidden.</summary>
        public bool OrderKnown;
        /// <summary>Seconds from the order coming in to serving.</summary>
        public float TimeTaken;

        // What happened
        public List<VisitEvent> Speech = new List<VisitEvent>();
        public List<VisitEvent> Cooking = new List<VisitEvent>();

        // Result
        /// <summary>
        /// What was served as a recipe (cooked perfectly): the menu dish it makes, or a dish of its own,
        /// "Nameless" until the player names it. Its steps recreate the dish.
        /// </summary>
        public RecipeDefinition Dish;
        public List<IngredientPortion> Served = new List<IngredientPortion>();
        public FlavourProfile Flavour;
        public string BestZone;
        public Verdict Verdict;
        public bool Exact;
        public Similarity Similarity;
        public float Price;
        public float PayShare;
        public float Payment;
        public float Tip;
        public float IngredientCost;

        /// <summary>The desire clue they gave, or null if they didn't (still a stranger, or no desire).</summary>
        public string Clue;
        public Warmth Warmth = Warmth.First;
        /// <summary>Radar distance to their secret desire; hidden from the player, used to find the closest dish.</summary>
        public float DesireDistance = float.PositiveInfinity;

        public float Paid => Payment + Tip;
    }
}
