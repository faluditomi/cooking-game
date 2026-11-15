using System.Collections.Generic;

namespace Cooking.Prototype
{
    public enum Verdict
    {
        /// <summary>Nothing on the plate, or it tastes nothing like what they wanted. No pay.</summary>
        Disliked,
        /// <summary>Contains something they will not eat. No pay.</summary>
        Refused,
        /// <summary>Exactly what they ordered, cooked right. Paid.</summary>
        Ordered,
        /// <summary>Not what they ordered, but in the same desirable zone. Paid, maybe tipped.</summary>
        LikedSubstitute,
        /// <summary>Hits their secret desire. Paid and tipped generously.</summary>
        DreamMeal,
    }

    public struct JudgeResult
    {
        public Verdict Verdict;
        public FlavourProfile Dish;
        public FlavourProfile Ordered;
        public bool ExactMatch;
        public FlavourZoneMatch BestZone;
        public FlavourZoneMatch SharedZone;
        /// <summary>Score against the customer's secret desire, or -1 when they have none.</summary>
        public float DesireScore;
        public IngredientDefinition RefusedIngredient;
        /// <summary>Menu price of the ordered dish.</summary>
        public float Price;
        public float Payment;
        public float Tip;

        public bool IsPaid => Payment > 0f;
    }

    /// <summary>
    /// The design doc's payment rules: an exact order gets paid; an unordered dish still gets paid
    /// if its flavour sits in the same desirable zone as the order; a dish that hits the customer's
    /// secret desire gets paid and tipped.
    /// </summary>
    public static class DishJudge
    {
        public struct Settings
        {
            /// <summary>Menu price = ingredient cost × margin.</summary>
            public float Margin;
            /// <summary>Shared-zone score a substitute needs to be accepted.</summary>
            public float LikeThreshold;
            /// <summary>Tip for a substitute that is fully inside the shared zone, as a share of the price.</summary>
            public float SubstituteTip;
            /// <summary>Tip for a dream meal, as a share of the price.</summary>
            public float DreamTip;

            public static Settings Default => new Settings { Margin = 1.6f, LikeThreshold = 0.5f, SubstituteTip = 0.25f, DreamTip = 1f };
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
            result.BestZone = FlavourEvaluator.FindBestZone(result.Dish, zones, falloff);
            result.SharedZone = FlavourEvaluator.FindSharedZone(result.Ordered, result.Dish, zones, falloff);

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
                result.Payment = result.Price;
                result.Tip = result.Price * settings.DreamTip;
            }
            else if(result.ExactMatch)
            {
                result.Verdict = Verdict.Ordered;
                result.Payment = result.Price;
            }
            else if(result.SharedZone.IsMatch && result.SharedZone.Score >= settings.LikeThreshold)
            {
                result.Verdict = Verdict.LikedSubstitute;
                result.Payment = result.Price;
                result.Tip = result.SharedZone.IsInside ? result.Price * settings.SubstituteTip : 0f;
            }

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
