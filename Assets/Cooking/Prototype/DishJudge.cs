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
        /// <summary>Hits their secret desire. Paid and tipped generously.</summary>
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
        /// <summary>Served after the order timer ran out: they still pay, but never tip and aren't happy.</summary>
        public bool Late;

        public bool IsPaid => Payment > 0f;

        /// <summary>The top tiers on time: exact, very close, or their dream meal. Counts towards warming up.</summary>
        public bool IsHappy => IsPaid && !Late && (Verdict == Verdict.Ordered || Verdict == Verdict.VeryClose || Verdict == Verdict.DreamMeal);
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

            public static Settings Default => new Settings
            {
                Margin = 1.6f,
                Similarity = DishSimilarity.Settings.Default,
                VeryCloseAt = 0.8f,
                VeryClosePay = 0.8f,
                CloseEnoughAt = 0.55f,
                CloseEnoughPay = 0.6f,
                DreamTip = 1f,
            };
        }

        public static float MenuPrice(RecipeDefinition recipe, float margin)
        {
            return recipe != null ? recipe.CalculateIngredientCost() * margin : 0f;
        }

        /// <param name="late">Served after the order timer ran out. Payment is unchanged, but there is no tip.</param>
        public static JudgeResult Judge(RecipeDefinition order, IReadOnlyList<IngredientPortion> dish, Customer customer,
            IReadOnlyList<FlavourZone> zones, FlavourRules rules, Settings settings, bool late = false)
        {
            JudgeResult result = JudgeFood(order, dish, customer, zones, rules, settings);
            result.Late = late;
            if(late) result.Tip = 0f;
            return result;
        }

        private static JudgeResult JudgeFood(RecipeDefinition order, IReadOnlyList<IngredientPortion> dish, Customer customer,
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

            result.RefusedIngredient = FindRefused(dish, customer);

            if(result.RefusedIngredient != null)
            {
                result.Verdict = Verdict.Refused;
            }
            else if(result.DesireScore >= 1f)
            {
                result.Verdict = Verdict.DreamMeal;
                result.PayShare = 1f;
                result.Tip = result.Price * settings.DreamTip;
            }
            else if(result.ExactMatch)
            {
                result.Verdict = Verdict.Ordered;
                result.PayShare = 1f;
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

        private static bool HasFood(IReadOnlyList<IngredientPortion> dish)
        {
            if(dish == null) return false;

            for(int i = 0; i < dish.Count; i++)
            {
                if(dish[i] != null && dish[i].IsValid) return true;
            }

            return false;
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
