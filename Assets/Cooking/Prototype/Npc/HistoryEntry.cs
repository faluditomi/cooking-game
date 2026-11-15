using System.Collections.Generic;

namespace Cooking.Prototype
{
    /// <summary>
    /// One served dish in the cooking history (design doc: Recipe App → Cooking history): who it
    /// was for, what was ordered and served, how it went, what they said, and any clue towards
    /// their secret desire.
    /// </summary>
    public class HistoryEntry
    {
        public int Day;
        public Customer Customer;
        public NpcStage Stage;
        public string Ordered;
        public List<IngredientPortion> Served = new List<IngredientPortion>();
        public FlavourProfile Flavour;
        public Verdict Verdict;
        public float Similarity;
        public float PayShare;
        public float Paid;
        public bool Late;
        /// <summary>Everything the customer said during the visit, in order.</summary>
        public List<string> Lines = new List<string>();
        /// <summary>The desire clue they gave, or null if they didn't (not warmed up yet, or no desire).</summary>
        public string Clue;
        public Warmth Warmth = Warmth.First;
        /// <summary>Radar distance to their secret desire; hidden from the player, used to find the closest dish.</summary>
        public float DesireDistance = float.PositiveInfinity;
    }
}
