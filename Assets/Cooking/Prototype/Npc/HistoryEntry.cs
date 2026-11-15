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
        public string Ordered;
        public string OrderLine;
        public FlavourProfile OrderedFlavour;
        public bool Timed;
        public float TimeAllowed;
        /// <summary>Seconds from taking the order to serving.</summary>
        public float TimeTaken;

        // What happened
        public List<VisitEvent> Speech = new List<VisitEvent>();
        public List<VisitEvent> Cooking = new List<VisitEvent>();

        // Result
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
        public bool Late;

        /// <summary>The desire clue they gave, or null if they didn't (not warmed up yet, or no desire).</summary>
        public string Clue;
        public Warmth Warmth = Warmth.First;
        /// <summary>Radar distance to their secret desire; hidden from the player, used to find the closest dish.</summary>
        public float DesireDistance = float.PositiveInfinity;

        public float Paid => Payment + Tip;
    }
}
