using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class CookingPrototypeTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private T Create<T>() where T : ScriptableObject
        {
            T obj = ScriptableObject.CreateInstance<T>();
            created.Add(obj);
            return obj;
        }

        private IngredientDefinition Ingredient(string name, FlavourProfile flavour, float price = 1f)
        {
            IngredientDefinition ingredient = Create<IngredientDefinition>();
            ingredient.Configure(name, flavour, 1f, default, price);
            return ingredient;
        }

        private RecipeDefinition Recipe(params IngredientPortion[] portions)
        {
            RecipeDefinition recipe = Create<RecipeDefinition>();
            recipe.Configure("Recipe", "Test", portions);
            return recipe;
        }

        private FlavourZone Zone(string name, FlavourProfile min, FlavourProfile max)
        {
            FlavourZone zone = Create<FlavourZone>();
            zone.Configure(name, min, max);
            return zone;
        }

        private static FlavourProfile Open => FlavourProfile.Filled(FlavourProfile.MaxValue);

        // ------------------------------------------------------------------ Recipe matching

        [Test]
        public void Recipe_MatchesSameDish_EvenWhenPortionsAreSplit()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            IngredientDefinition salt = Ingredient("Salt", FlavourProfile.Zero);
            RecipeDefinition recipe = Recipe(new IngredientPortion(rice, 2, cooking: CookMethods.Fried), new IngredientPortion(salt, 1, cooking: CookMethods.Fried));

            List<IngredientPortion> dish = new List<IngredientPortion>
            {
                new IngredientPortion(salt, 1, cooking: CookMethods.Fried),
                new IngredientPortion(rice, 1, cooking: CookMethods.Fried),
                new IngredientPortion(rice, 1, cooking: CookMethods.Fried),
            };

            Assert.IsTrue(recipe.Matches(dish));
        }

        [Test]
        public void Recipe_DoesNotMatch_WrongQuantityPrepCookingOrExtras()
        {
            IngredientDefinition onion = Ingredient("Onion", FlavourProfile.Zero);
            IngredientDefinition salt = Ingredient("Salt", FlavourProfile.Zero);
            RecipeDefinition recipe = Recipe(new IngredientPortion(onion, 1, PrepSteps.Peeled | PrepSteps.Sliced, CookMethods.Fried));

            Assert.IsFalse(recipe.Matches(new[] { new IngredientPortion(onion, 2, PrepSteps.Peeled | PrepSteps.Sliced, CookMethods.Fried) }), "quantity");
            Assert.IsFalse(recipe.Matches(new[] { new IngredientPortion(onion, 1, PrepSteps.Sliced, CookMethods.Fried) }), "prep");
            Assert.IsFalse(recipe.Matches(new[] { new IngredientPortion(onion, 1, PrepSteps.Peeled | PrepSteps.Sliced, CookMethods.Boiled) }), "cooking");
            Assert.IsFalse(recipe.Matches(new[]
            {
                new IngredientPortion(onion, 1, PrepSteps.Peeled | PrepSteps.Sliced, CookMethods.Fried),
                new IngredientPortion(salt, 1),
            }), "extra");
            Assert.IsFalse(recipe.Matches(new List<IngredientPortion>()), "empty");
        }

        [Test]
        public void Recipe_DoesNotMatch_WhenSomethingIsBadlyCooked()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            RecipeDefinition recipe = Recipe(new IngredientPortion(rice, 1, cooking: CookMethods.Fried));

            Assert.IsFalse(recipe.Matches(new[] { new IngredientPortion(rice, 1, cooking: CookMethods.Fried, doneness: Doneness.Burnt) }));
        }

        // ------------------------------------------------------------------ Stations

        [Test]
        public void Wok_DonenessFollowsTheBar_AndBurnsAtTheEnd()
        {
            CookingStation wok = CookingStation.CreateWok(10f);

            Assert.AreEqual(Doneness.Undercooked, wok.Evaluate(0.3f));
            Assert.AreEqual(Doneness.Perfect, wok.Evaluate(0.6f));
            Assert.AreEqual(Doneness.Perfect, wok.Evaluate(0.95f));
            Assert.AreEqual(Doneness.Burnt, wok.Evaluate(1f));
        }

        [Test]
        public void Pot_HasNarrowPerfectBand_ThenOvercooks()
        {
            CookingStation pot = CookingStation.CreatePot(10f);

            Assert.AreEqual(Doneness.Undercooked, pot.Evaluate(0.3f));
            Assert.AreEqual(Doneness.Perfect, pot.Evaluate(0.5f));
            Assert.AreEqual(Doneness.Overcooked, pot.Evaluate(0.8f));
            Assert.AreEqual(Doneness.Overcooked, pot.Evaluate(1f));
        }

        [Test]
        public void Station_StacksPortions_CooksThemAndEmpties()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            CookingStation wok = CookingStation.CreateWok(10f);

            Assert.IsTrue(wok.Add(new IngredientPortion(rice, 1)));
            Assert.IsTrue(wok.Add(new IngredientPortion(rice, 1)));
            Assert.AreEqual(1, wok.Contents.Count);
            Assert.AreEqual(2, wok.Contents[0].quantity);

            Assert.IsTrue(wok.StartCooking());
            Assert.IsFalse(wok.Add(new IngredientPortion(rice, 1)), "cannot add while cooking");
            wok.Tick(6f);

            List<IngredientPortion> cooked = wok.TakeOff();

            Assert.AreEqual(1, cooked.Count);
            Assert.AreEqual(CookMethods.Fried, cooked[0].cooking);
            Assert.AreEqual(Doneness.Perfect, cooked[0].doneness);
            Assert.IsTrue(wok.IsEmpty);
            Assert.IsFalse(wok.IsCooking);
        }

        [Test]
        public void Shake_OnlyWorksInTheDangerArea()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            CookingStation wok = CookingStation.CreateWok(10f);
            wok.Add(new IngredientPortion(rice, 1));
            wok.StartCooking();

            wok.Tick(5f);
            Assert.IsFalse(wok.Shake(), "not in danger yet");

            wok.Tick(4f);
            float before = wok.Progress;
            Assert.IsTrue(wok.Shake());
            Assert.Less(wok.Progress, before);
            Assert.AreEqual(Doneness.Perfect, wok.CurrentDoneness);
        }

        [Test]
        public void Pot_CannotBeShaken()
        {
            IngredientDefinition potato = Ingredient("Potato", FlavourProfile.Zero);
            CookingStation pot = CookingStation.CreatePot(10f);
            pot.Add(new IngredientPortion(potato, 1));
            pot.StartCooking();
            pot.Tick(9f);

            Assert.IsFalse(pot.Shake());
        }

        // ------------------------------------------------------------------ Judging

        private struct JudgeFixture
        {
            public IngredientDefinition Salty;
            public IngredientDefinition Sweet;
            public RecipeDefinition Order;
            public FlavourZone SaltyZone;
            public FlavourZone SweetZone;
            public FlavourZone[] Zones;
        }

        private JudgeFixture Fixture()
        {
            JudgeFixture f = new JudgeFixture();
            f.Salty = Ingredient("Salty", new FlavourProfile { salty = 8f }, price: 2f);
            f.Sweet = Ingredient("Sweet", new FlavourProfile { sweet = 8f }, price: 3f);
            f.Order = Recipe(new IngredientPortion(f.Salty, 1, cooking: CookMethods.Boiled));
            f.SaltyZone = Zone("Salty", new FlavourProfile { salty = 6f }, Open);
            f.SweetZone = Zone("Sweet", new FlavourProfile { sweet = 6f }, Open);
            f.Zones = new[] { f.SaltyZone, f.SweetZone };
            return f;
        }

        private static DishJudge.Settings Settings
        {
            get
            {
                DishJudge.Settings settings = DishJudge.Settings.Default;
                settings.Margin = 2f;
                return settings;
            }
        }

        [Test]
        public void Judge_ExactOrder_IsPaidMenuPrice()
        {
            JudgeFixture f = Fixture();
            JudgeResult result = DishJudge.Judge(f.Order, f.Order.CreatePerfectPortions(), new Customer(), f.Zones, null, Settings);

            Assert.AreEqual(Verdict.Ordered, result.Verdict);
            Assert.AreEqual(4f, result.Payment, 0.0001f);
            Assert.AreEqual(0f, result.Tip);
        }

        [Test]
        public void Judge_RightIngredientCookedDifferently_IsCloseEnough()
        {
            JudgeFixture f = Fixture();
            // Tastes the same (no rules), but raw instead of boiled: flavour 100%, ingredients 50% → 75%.
            List<IngredientPortion> dish = new List<IngredientPortion> { new IngredientPortion(f.Salty, 1) };

            JudgeResult result = DishJudge.Judge(f.Order, dish, new Customer(), f.Zones, null, Settings);

            Assert.AreEqual(Verdict.CloseEnough, result.Verdict);
            Assert.AreEqual(0.75f, result.Similarity.Combined, 0.0001f);
            Assert.AreEqual(0.6f, result.PayShare, 0.0001f);
            Assert.AreEqual(2.4f, result.Payment, 0.0001f);
            Assert.AreEqual(0f, result.Tip);
            Assert.IsTrue(result.IsPaid);
            Assert.IsFalse(result.IsHappy, "close enough is paid but lukewarm");
        }

        [Test]
        public void Judge_MostlyRight_IsVeryClose_AndHappy()
        {
            JudgeFixture f = Fixture();
            RecipeDefinition order = Recipe(new IngredientPortion(f.Salty, 4, cooking: CookMethods.Boiled));
            // One of four units raw: flavour 100%, ingredients (3 + 0.5) / 4 = 87.5% → 93.75%.
            List<IngredientPortion> dish = new List<IngredientPortion>
            {
                new IngredientPortion(f.Salty, 3, cooking: CookMethods.Boiled),
                new IngredientPortion(f.Salty, 1),
            };

            JudgeResult result = DishJudge.Judge(order, dish, new Customer(), f.Zones, null, Settings);

            Assert.AreEqual(Verdict.VeryClose, result.Verdict);
            Assert.AreEqual(16f * 0.8f, result.Payment, 0.0001f);
            Assert.IsTrue(result.IsHappy);
        }

        [Test]
        public void Judge_BurntOrder_IsNoLongerExact_ButCanStillBePaid()
        {
            JudgeFixture f = Fixture();
            RecipeDefinition order = Recipe(new IngredientPortion(f.Salty, 4, cooking: CookMethods.Fried));
            List<IngredientPortion> dish = new List<IngredientPortion> { new IngredientPortion(f.Salty, 4, cooking: CookMethods.Fried, doneness: Doneness.Burnt) };

            JudgeResult result = DishJudge.Judge(order, dish, new Customer(), f.Zones, null, Settings);

            Assert.IsFalse(result.ExactMatch);
            Assert.AreEqual(0.5f, result.Similarity.Ingredients, 0.0001f, "badly cooked units count half");
            Assert.AreEqual(Verdict.CloseEnough, result.Verdict);
        }

        [Test]
        public void Similarity_ExtraAndMissingUnits_PullTheScoreDown()
        {
            JudgeFixture f = Fixture();
            IngredientPortion[] ordered = { new IngredientPortion(f.Salty, 2, cooking: CookMethods.Boiled) };

            Assert.AreEqual(1f, DishSimilarity.IngredientMatch(ordered, new[] { new IngredientPortion(f.Salty, 2, cooking: CookMethods.Boiled) }), 0.0001f);
            Assert.AreEqual(0.5f, DishSimilarity.IngredientMatch(ordered, new[] { new IngredientPortion(f.Salty, 1, cooking: CookMethods.Boiled) }), 0.0001f, "missing one");
            Assert.AreEqual(0.5f, DishSimilarity.IngredientMatch(ordered, new[]
            {
                new IngredientPortion(f.Salty, 2, cooking: CookMethods.Boiled),
                new IngredientPortion(f.Sweet, 2, cooking: CookMethods.Boiled),
            }), 0.0001f, "two extra");
            Assert.AreEqual(0f, DishSimilarity.IngredientMatch(ordered, new[] { new IngredientPortion(f.Sweet, 2, cooking: CookMethods.Boiled) }), 0.0001f);
        }

        [Test]
        public void Judge_DishInAnotherZone_IsNotPaid()
        {
            JudgeFixture f = Fixture();
            List<IngredientPortion> dish = new List<IngredientPortion> { new IngredientPortion(f.Sweet, 1) };

            JudgeResult result = DishJudge.Judge(f.Order, dish, new Customer(), f.Zones, null, Settings);

            Assert.AreEqual(Verdict.Disliked, result.Verdict);
            Assert.IsFalse(result.IsPaid);
        }

        [Test]
        public void Judge_SecretDesire_BeatsTheOrder()
        {
            JudgeFixture f = Fixture();
            Customer customer = new Customer { secretDesire = f.SweetZone };
            List<IngredientPortion> dish = new List<IngredientPortion> { new IngredientPortion(f.Sweet, 1) };

            JudgeResult result = DishJudge.Judge(f.Order, dish, customer, f.Zones, null, Settings);

            Assert.AreEqual(Verdict.DreamMeal, result.Verdict);
            Assert.AreEqual(4f, result.Payment, 0.0001f);
            Assert.AreEqual(4f, result.Tip, 0.0001f);
        }

        [Test]
        public void Judge_RefusedIngredient_IsNeverPaid()
        {
            JudgeFixture f = Fixture();
            Customer customer = new Customer { refuses = new List<IngredientDefinition> { f.Salty } };

            JudgeResult result = DishJudge.Judge(f.Order, f.Order.CreatePerfectPortions(), customer, f.Zones, null, Settings);

            Assert.AreEqual(Verdict.Refused, result.Verdict);
            Assert.AreSame(f.Salty, result.RefusedIngredient);
            Assert.IsFalse(result.IsPaid);
        }

        [Test]
        public void Judge_EmptyPlate_IsDisliked()
        {
            JudgeFixture f = Fixture();

            JudgeResult result = DishJudge.Judge(f.Order, new List<IngredientPortion>(), new Customer(), f.Zones, null, Settings);

            Assert.AreEqual(Verdict.Disliked, result.Verdict);
            Assert.IsFalse(result.IsPaid);
        }
    }
}
