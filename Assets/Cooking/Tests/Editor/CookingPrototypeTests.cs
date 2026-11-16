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
        public void Wok_ZonesAreGreyGreenRedBlack()
        {
            CookingStation wok = CookingStation.CreateWok(10f);

            Assert.AreEqual(Doneness.Undercooked, wok.Evaluate(0.3f));
            Assert.AreEqual(Doneness.Perfect, wok.Evaluate(0.6f));
            Assert.AreEqual(Doneness.Overcooked, wok.Evaluate(0.8f));
            Assert.AreEqual(Doneness.Burnt, wok.Evaluate(0.95f));
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
        public void Station_TakesOneBatch_CooksItAndEmpties()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            CookingStation pot = CookingStation.CreatePot(10f);

            Assert.IsTrue(pot.AddAll(new[] { new IngredientPortion(rice, 2) }));
            Assert.IsFalse(pot.CanAddAll(new[] { new IngredientPortion(rice, 1) }, out _), "a dish goes in all at once");

            Assert.IsTrue(pot.StartCooking());
            Assert.IsFalse(pot.Add(new IngredientPortion(rice, 1)), "cannot add while cooking");
            pot.Tick(5f);

            List<IngredientPortion> cooked = pot.TakeOff();

            Assert.AreEqual(1, cooked.Count);
            Assert.AreEqual(2, cooked[0].quantity);
            Assert.AreEqual(CookMethods.Boiled, cooked[0].cooking);
            Assert.AreEqual(Doneness.Perfect, cooked[0].doneness);
            Assert.IsTrue(pot.IsEmpty);
            Assert.IsFalse(pot.IsCooking);
            Assert.IsTrue(pot.CanAddAll(new[] { new IngredientPortion(rice, 1) }, out _), "empty again");
        }

        [Test]
        public void Wok_NeedsAStirPerUnit_InTheGreen()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            CookingStation wok = CookingStation.CreateWok(10f, seed: 1);
            wok.AddAll(new[] { new IngredientPortion(rice, 3) });
            wok.StartCooking();

            wok.Tick(3f); // 30%: grey
            Assert.AreEqual(ShakeResult.TooEarly, wok.Shake());
            Assert.AreEqual(0f, wok.Progress, "too early sends the bar back to the start");

            wok.Tick(6f); // 60%: green
            Assert.AreEqual(ShakeResult.Stirred, wok.Shake());
            Assert.AreEqual(wok.PerfectStart * 0.5f, wok.Progress, 0.0001f, "back to the middle of the grey");

            wok.Tick(3.5f); // 62.5%: green
            Assert.AreEqual(ShakeResult.Stirred, wok.Shake());

            List<IngredientPortion> cooked = wok.TakeOff();
            Assert.AreEqual(2, Count(cooked, Doneness.Perfect));
            Assert.AreEqual(1, Count(cooked, Doneness.Undercooked), "the unit never stirred is undercooked");
        }

        [Test]
        public void Wok_RedStirOvercooks_BlackBurns_AndTheEndKeepsBurning()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            CookingStation wok = CookingStation.CreateWok(10f, seed: 2);
            wok.AddAll(new[] { new IngredientPortion(rice, 4) });
            wok.StartCooking();

            wok.Tick(8f); // 80%: red
            Assert.AreEqual(ShakeResult.StirredOverdone, wok.Shake());
            Assert.AreEqual(1, wok.OverdoneCount);

            wok.Tick(6.5f); // from 27.5% to 92.5%: black burns one
            Assert.AreEqual(1, wok.BurntCount);

            wok.Tick(1f); // at the end
            wok.Tick(2f); // two seconds at the end: another
            Assert.AreEqual(2, wok.BurntCount);

            List<IngredientPortion> cooked = wok.TakeOff();
            Assert.AreEqual(4, Count(cooked, Doneness.Overcooked) + Count(cooked, Doneness.Burnt) + Count(cooked, Doneness.Undercooked));
            Assert.AreEqual(2, Count(cooked, Doneness.Burnt));
        }

        private static int Count(List<IngredientPortion> portions, Doneness doneness)
        {
            int count = 0;
            foreach(IngredientPortion portion in portions) if(portion.doneness == doneness) count += portion.quantity;
            return count;
        }

        [Test]
        public void Pot_CannotBeShaken()
        {
            IngredientDefinition potato = Ingredient("Potato", FlavourProfile.Zero);
            CookingStation pot = CookingStation.CreatePot(10f);
            pot.Add(new IngredientPortion(potato, 1));
            pot.StartCooking();
            pot.Tick(9f);

            Assert.AreEqual(ShakeResult.None, pot.Shake());
        }

        [Test]
        public void Seasoning_CountsForTheRecipe_CookedInOrAddedAfter()
        {
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            IngredientDefinition salt = ScriptableObject.CreateInstance<IngredientDefinition>();
            created.Add(salt);
            salt.Configure("Salt", new FlavourProfile { salty = 9f }, 0.1f, newIsSeasoning: true);

            RecipeDefinition recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            created.Add(recipe);
            recipe.Configure("Salty rice", "Test", new[] { new IngredientPortion(rice, 1, cooking: CookMethods.Fried), new IngredientPortion(salt, 1, cooking: CookMethods.Fried) });

            IngredientPortion friedRice = new IngredientPortion(rice, 1, PrepSteps.None, CookMethods.Fried, Doneness.Perfect);
            Assert.IsTrue(recipe.Matches(new[] { friedRice, new IngredientPortion(salt, 1, PrepSteps.None, CookMethods.Fried, Doneness.Perfect) }));
            Assert.IsTrue(recipe.Matches(new[] { friedRice, new IngredientPortion(salt, 1) }), "salt sprinkled on after counts too");
            Assert.IsFalse(recipe.Matches(new[] { new IngredientPortion(rice, 1), new IngredientPortion(salt, 1) }), "the rice still has to be fried");
        }

        [Test]
        public void Seasoning_GivesHalfItsTaste_WithoutThePrepItNeeds()
        {
            IngredientDefinition chilli = ScriptableObject.CreateInstance<IngredientDefinition>();
            IngredientDefinition rice = Ingredient("Rice", FlavourProfile.Zero);
            created.Add(chilli);
            chilli.Configure("Chilli", new FlavourProfile { pain = 8f }, 0.2f, newIsSeasoning: true, newFullFlavourPrep: PrepSteps.Grated);

            float grated = FlavourCalculator.Calculate(new[] { new IngredientPortion(rice, 1), new IngredientPortion(chilli, 1, PrepSteps.Grated) }, null).pain;
            float whole = FlavourCalculator.Calculate(new[] { new IngredientPortion(rice, 1), new IngredientPortion(chilli, 1) }, null).pain;

            Assert.AreEqual(grated * 0.5f, whole, 0.0001f);
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
            Assert.IsFalse(result.IsLiked, "close enough is paid but lukewarm");
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
            Assert.IsTrue(result.IsLiked);
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
