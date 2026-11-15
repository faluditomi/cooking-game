using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class FlavourCalculatorTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private IngredientDefinition Ingredient(string name, FlavourProfile flavour, float bulk = 1f, FlavourProfile rawDelta = default, float price = 1f)
        {
            IngredientDefinition ingredient = ScriptableObject.CreateInstance<IngredientDefinition>();
            ingredient.Configure(name, flavour, bulk, rawDelta, price);
            created.Add(ingredient);
            return ingredient;
        }

        private FlavourRules Rules()
        {
            FlavourRules rules = ScriptableObject.CreateInstance<FlavourRules>();
            created.Add(rules);
            return rules;
        }

        [Test]
        public void EmptyDish_IsZero()
        {
            Assert.AreEqual(FlavourProfile.Zero, FlavourCalculator.Calculate(new List<IngredientPortion>(), null));
            Assert.AreEqual(FlavourProfile.Zero, FlavourCalculator.Calculate(null, null));
        }

        [Test]
        public void SingleBulkOneIngredient_TastesLikeItsFlavour()
        {
            FlavourProfile flavour = new FlavourProfile { savoury = 5f, nutrition = 4f, texture = 6f };
            IngredientDefinition mushroom = Ingredient("Mushroom", flavour);

            FlavourProfile result = FlavourCalculator.Calculate(new List<IngredientPortion> { new IngredientPortion(mushroom, 3) }, null);

            Assert.IsTrue(result.Approximately(flavour), result.ToString());
        }

        [Test]
        public void Seasoning_ConcentratesIntoDish_WithoutDilutingIt()
        {
            IngredientDefinition rice = Ingredient("Rice", new FlavourProfile { nutrition = 6f, texture = 4f });
            IngredientDefinition salt = Ingredient("Salt", new FlavourProfile { salty = 10f }, bulk: 0.1f);

            FlavourProfile result = FlavourCalculator.Calculate(new List<IngredientPortion>
            {
                new IngredientPortion(rice, 2),
                new IngredientPortion(salt, 1),
            }, null);

            // Total bulk 2.1. Salty = 10 / 2.1, nutrition = 12 / 2.1.
            Assert.AreEqual(10f / 2.1f, result.salty, 0.0001f);
            Assert.AreEqual(12f / 2.1f, result.nutrition, 0.0001f);
            // Texture is a bulk-weighted average: (4 × 2 + 0 × 0.1) / 2.1.
            Assert.AreEqual(8f / 2.1f, result.texture, 0.0001f);
        }

        [Test]
        public void MoreOfAnIngredient_PullsDishTowardIt()
        {
            IngredientDefinition sweet = Ingredient("Sweet", new FlavourProfile { sweet = 8f });
            IngredientDefinition plain = Ingredient("Plain", new FlavourProfile { nutrition = 5f });

            float oneSweet = FlavourCalculator.Calculate(new List<IngredientPortion> { new IngredientPortion(sweet, 1), new IngredientPortion(plain, 2) }, null).sweet;
            float twoSweet = FlavourCalculator.Calculate(new List<IngredientPortion> { new IngredientPortion(sweet, 2), new IngredientPortion(plain, 2) }, null).sweet;

            Assert.Greater(twoSweet, oneSweet);
        }

        [Test]
        public void Result_IsClampedToScale()
        {
            IngredientDefinition salt = Ingredient("Salt", new FlavourProfile { salty = 10f }, bulk: 0.1f);

            FlavourProfile result = FlavourCalculator.Calculate(new List<IngredientPortion> { new IngredientPortion(salt, 5) }, null);

            Assert.AreEqual(FlavourProfile.MaxValue, result.salty);
        }

        [Test]
        public void InvalidPortions_AreIgnored()
        {
            IngredientDefinition mushroom = Ingredient("Mushroom", new FlavourProfile { savoury = 5f });

            FlavourProfile result = FlavourCalculator.Calculate(new List<IngredientPortion>
            {
                new IngredientPortion(mushroom, 1),
                new IngredientPortion(mushroom, 0),
                new IngredientPortion(null, 3),
                null,
            }, null);

            Assert.AreEqual(5f, result.savoury, 0.0001f);
        }

        [Test]
        public void Processing_AppliesPrepCookingAndDoneness()
        {
            FlavourRules rules = Rules();
            rules.sliced = new FlavourProfile { texture = -1f };
            rules.fried = new FlavourProfile { savoury = 2f, texture = 2f };
            rules.burnt = new FlavourProfile { bitter = 6f };

            IngredientDefinition beef = Ingredient("Beef", new FlavourProfile { savoury = 5f, texture = 5f });
            IngredientPortion portion = new IngredientPortion(beef, 1, PrepSteps.Sliced, CookMethods.Fried, Doneness.Burnt);

            FlavourProfile unit = FlavourCalculator.GetUnitProfile(portion, rules);

            Assert.AreEqual(7f, unit.savoury, 0.0001f);
            Assert.AreEqual(6f, unit.texture, 0.0001f);
            Assert.AreEqual(6f, unit.bitter, 0.0001f);
        }

        [Test]
        public void TasteDeltas_ScaleWithBulk_TextureDeltasDoNot()
        {
            FlavourRules rules = Rules();
            rules.fried = new FlavourProfile { savoury = 2f, texture = 2f };

            IngredientDefinition sugar = Ingredient("Sugar", new FlavourProfile { sweet = 10f }, bulk: 0.1f);
            FlavourProfile unit = FlavourCalculator.GetUnitProfile(new IngredientPortion(sugar, 1, cooking: CookMethods.Fried), rules);

            Assert.AreEqual(0.2f, unit.savoury, 0.0001f);
            Assert.AreEqual(2f, unit.texture, 0.0001f);
            Assert.AreEqual(10f, unit.sweet, 0.0001f);
        }

        [Test]
        public void Doneness_IsIgnoredForRawIngredients()
        {
            FlavourRules rules = Rules();
            rules.burnt = new FlavourProfile { bitter = 6f };

            IngredientDefinition onion = Ingredient("Onion", new FlavourProfile { sweet = 3f });
            IngredientPortion portion = new IngredientPortion(onion, 1, PrepSteps.None, CookMethods.None, Doneness.Burnt);

            Assert.AreEqual(0f, FlavourCalculator.GetUnitProfile(portion, rules).bitter);
        }

        [Test]
        public void RawDelta_AppliesOnlyWhenUncooked()
        {
            FlavourRules rules = Rules();
            IngredientDefinition chicken = Ingredient("Chicken", new FlavourProfile { savoury = 4f, nutrition = 6f },
                rawDelta: new FlavourProfile { bitter = 4f, nutrition = -4f });

            FlavourProfile raw = FlavourCalculator.GetUnitProfile(new IngredientPortion(chicken, 1), rules);
            FlavourProfile boiled = FlavourCalculator.GetUnitProfile(new IngredientPortion(chicken, 1, cooking: CookMethods.Boiled), rules);

            Assert.AreEqual(4f, raw.bitter, 0.0001f);
            Assert.AreEqual(2f, raw.nutrition, 0.0001f);
            Assert.AreEqual(0f, boiled.bitter, 0.0001f);
            Assert.AreEqual(6f, boiled.nutrition, 0.0001f);
        }

        [Test]
        public void NegativeDeltas_DoNotPushUnitBelowZero()
        {
            FlavourRules rules = Rules();
            rules.boiled = new FlavourProfile { texture = -20f };

            IngredientDefinition potato = Ingredient("Potato", new FlavourProfile { texture = 6f });

            FlavourProfile unit = FlavourCalculator.GetUnitProfile(new IngredientPortion(potato, 1, cooking: CookMethods.Boiled), rules);

            Assert.AreEqual(0f, unit.texture);
        }

        [Test]
        public void IngredientCost_SumsPriceTimesQuantity()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero, price: 1.5f);
            IngredientDefinition beef = Ingredient("Beef", FlavourProfile.Zero, price: 8f);

            float cost = FlavourCalculator.CalculateIngredientCost(new List<IngredientPortion>
            {
                new IngredientPortion(rice, 2),
                new IngredientPortion(beef, 1),
            });

            Assert.AreEqual(11f, cost, 0.0001f);
        }
    }
}
