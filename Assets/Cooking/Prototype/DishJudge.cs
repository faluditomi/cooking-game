using System.Collections.Generic;

namespace Cooking.Prototype
{
    public enum Verdict
    {
        /// <summary>Nothing on the plate, or too different from what they ordered. No pay.</summary>
        Disliked,
        /// <summary>Contains something they will not eat. No pay.</summary>
        Refused,
        /// <summary>Exactly what they ordered, cooked right. Full price.</summary>
        Ordered,
        /// <summary>Not exact, but very close to the order. Most of the price, and they're happy.</summary>
        VeryClose,
        /// <summary>Close enough to pay for, but they're lukewarm about it.</summary>
        CloseEnough,
        /// <summary>Hits their secret desire (with what it needs). Paid and tipped generously, even over a refusal.</summary>
        DreamMeal,
    }

    public struct JudgeResult
    {
        public Verdict Verdict;
        public FlavourProfile Dish;
        public FlavourProfile Ordered;
        public bool ExactMatch;
        /// <summary>How close the plate is to the ordered recipe.</summary>
        public Similarity Similarity;
        public FlavourZoneMatch BestZone;
        /// <summary>Score against the customer's secret desire, or -1 when they have none.</summary>
        public float DesireScore;
        public IngredientDefinition RefusedIngredient;
        /// <summary>Menu price of the ordered dish.</summary>
        public float Price;
        /// <summary>Share of <see cref="Price"/> they pay for this tier (0..1).</summary>
        public float PayShare;
        public float Payment;
        public float Tip;

        public bool IsPaid => Payment > 0f;
        /// <summary>Not the order, but just as tasty: it lands in the same desirable flavour zone as the order did.</summary>
        public bool SameZone;
        /// <summary>Paid in full only because they don't know good food from bad yet (see <see cref="DishJudge.PayInFull"/>).</summary>
        public bool PaidAnyway;

        /// <summary>The top tiers: exact, very close, or their dream meal. Liked meals make them Familiar.</summary>
        public bool IsLiked => IsPaid && (Verdict == Verdict.Ordered || Verdict == Verdict.VeryClose || Verdict == Verdict.DreamMeal);
    }

    /// <summary>
    /// The payment rules. A dish that hits the customer's secret desire is a dream meal (paid and
    /// tipped). Otherwise it's judged on how close it is to the order (<see cref="DishSimilarity"/>):
    /// the exact recipe pays full price, very close and close enough pay part of it, and anything
    /// further away isn't paid for.
    /// </summary>
    public static class DishJudge
    {
        public struct Settings
        {
            /// <summary>Menu price = ingredient cost × margin.</summary>
            public float Margin;
            public DishSimilarity.Settings Similarity;
            /// <summary>Combined similarity for "very close".</summary>
            public float VeryCloseAt;
            /// <summary>Share of the price paid for "very close".</summary>
            public float VeryClosePay;
            /// <summary>Combined similarity for "close enough". Below this they don't pay.</summary>
            public float CloseEnoughAt;
            /// <summary>Share of the price paid for "close enough".</summary>
            public float CloseEnoughPay;
            /// <summary>Tip for a dream meal, as a share of the price.</summary>
            public float DreamTip;
            /// <summary>Tip for a dish that isn't the order but lands in the same flavour zone, as a share of the price.</summary>
            public float SameZoneTip;

            public static Settings Default => new Settings
            {
                Margin = 1.6f,
                Similarity = DishSimilarity.Settings.Default,
                VeryCloseAt = 0.8f,
                VeryClosePay = 0.8f,
                CloseEnoughAt = 0.55f,
                CloseEnoughPay = 0.6f,
                DreamTip = 1f,
                SameZoneTip = 0.2f,
            };
        }

        public static float MenuPrice(RecipeDefinition recipe, float margin)
        {
            return recipe != null ? recipe.CalculateIngredientCost() * margin : 0f;
        }

        public static JudgeResult Judge(RecipeDefinition order, IReadOnlyList<IngredientPortion> dish, Customer customer,
            IReadOnlyList<FlavourZone> zones, FlavourRules rules, Settings settings)
        {
            float falloff = rules != null ? rules.zoneFalloff : FlavourRules.DefaultZoneFalloff;

            JudgeResult result = new JudgeResult
            {
                Verdict = Verdict.Disliked,
                Dish = FlavourCalculator.Calculate(dish, rules),
                Ordered = order != null ? order.CalculateProfile(rules) : FlavourProfile.Zero,
                DesireScore = -1f,
                Price = MenuPrice(order, settings.Margin),
            };

            result.ExactMatch = order != null && order.Matches(dish);
            result.Similarity = DishSimilarity.Compare(order, dish, rules, settings.Similarity);
            result.BestZone = FlavourEvaluator.FindBestZone(result.Dish, zones, falloff);

            if(customer != null && customer.secretDesire != null)
            {
                result.DesireScore = FlavourEvaluator.Score(result.Dish, customer.secretDesire, falloff);
            }

            if(!HasFood(dish)) return result;

            // Their dream meal wins over anything they'd otherwise refuse.
            if(result.DesireScore >= 1f && HasAll(dish, customer.dreamNeeds))
            {
                result.Verdict = Verdict.DreamMeal;
                result.PayShare = 1f;
                result.Tip = result.Price * settings.DreamTip;
                result.Payment = result.Price;
                return result;
            }

            result.RefusedIngredient = FindRefused(dish, customer);

            if(result.RefusedIngredient != null)
            {
                result.Verdict = Verdict.Refused;
            }
            else if(result.ExactMatch)
            {
                result.Verdict = Verdict.Ordered;
                result.PayShare = 1f;
            }
            else if(InSameZone(result, order, zones, falloff) && WellCooked(dish))
            {
                // Design doc: a dish they didn't order is liked if its flavour falls in the same
                // desirable zone their order did. A different dish that's just as tasty is paid in
                // full and tipped; a near miss of the order is liked and paid as very close.
                bool different = result.Similarity.Combined < settings.CloseEnoughAt;
                result.Verdict = Verdict.VeryClose;
                result.SameZone = true;
                result.PayShare = different ? 1f : settings.VeryClosePay;
                if(different) result.Tip = result.Price * settings.SameZoneTip;
            }
            else if(result.Similarity.Combined >= settings.VeryCloseAt)
            {
                result.Verdict = Verdict.VeryClose;
                result.PayShare = settings.VeryClosePay;
            }
            else if(result.Similarity.Combined >= settings.CloseEnoughAt)
            {
                result.Verdict = Verdict.CloseEnough;
                result.PayShare = settings.CloseEnoughPay;
            }

            result.Payment = result.Price * result.PayShare;
            return result;
        }

        /// <summary>
        /// Full price for anything they'll eat (not a refusal), for a customer who can't tell good
        /// food from bad yet. What they thought of it (the verdict) stays as it was.
        /// </summary>
        public static JudgeResult PayInFull(JudgeResult result)
        {
            if(result.Verdict == Verdict.Refused || result.PayShare >= 1f) return result;

            result.PayShare = 1f;
            result.Payment = result.Price;
            result.PaidAnyway = true;
            return result;
        }

        /// <summary>Nothing on the plate burnt, overcooked or undercooked.</summary>
        private static bool WellCooked(IReadOnlyList<IngredientPortion> dish)
        {
            foreach(IngredientPortion portion in dish)
            {
                if(portion != null && portion.IsValid && !portion.IsRaw && portion.doneness != Doneness.Perfect) return false;
            }

            return true;
        }

        private static bool InSameZone(JudgeResult result, RecipeDefinition order, IReadOnlyList<FlavourZone> zones, float falloff)
        {
            if(order == null || zones == null || result.BestZone.Zone == null || result.BestZone.Score < 1f) return false;
            FlavourZoneMatch ordered = FlavourEvaluator.FindBestZone(result.Ordered, zones, falloff);
            return ordered.Zone == result.BestZone.Zone && ordered.Score >= 1f;
        }

        private static bool HasFood(IReadOnlyList<IngredientPortion> dish)
        {
            if(dish == null) return false;

            for(int i = 0; i < dish.Count; i++)
            {
                if(dish[i] != null && dish[i].IsValid) return true;
            }

            return false;
        }

        private static bool HasAll(IReadOnlyList<IngredientPortion> dish, List<IngredientDefinition> needs)
        {
            if(needs == null) return true;

            foreach(IngredientDefinition need in needs)
            {
                if(need == null) continue;
                bool found = false;

                for(int i = 0; i < dish.Count && !found; i++)
                {
                    found = dish[i] != null && dish[i].IsValid && dish[i].ingredient == need;
                }

                if(!found) return false;
            }

            return true;
        }

        private static IngredientDefinition FindRefused(IReadOnlyList<IngredientPortion> dish, Customer customer)
        {
            if(customer == null || customer.refuses == null || customer.refuses.Count == 0) return null;

            for(int i = 0; i < dish.Count; i++)
            {
                IngredientPortion portion = dish[i];
                if(portion != null && portion.IsValid && customer.refuses.Contains(portion.ingredient)) return portion.ingredient;
            }

            return null;
        }
    }
}
