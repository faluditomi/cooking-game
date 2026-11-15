using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class InventoryTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private IngredientDefinition Ingredient(string name, FlavourProfile flavour = default, float price = 1f)
        {
            IngredientDefinition ingredient = ScriptableObject.CreateInstance<IngredientDefinition>();
            ingredient.Configure(name, flavour, newPrice: price);
            created.Add(ingredient);
            return ingredient;
        }

        [Test]
        public void AddAndTake_TrackUnitsPerPrepState()
        {
            Inventory inventory = new Inventory();
            IngredientDefinition onion = Ingredient("Onion");

            inventory.Add(onion, PrepSteps.None, 3);
            inventory.Add(onion, PrepSteps.Peeled | PrepSteps.Sliced, 1);

            Assert.AreEqual(3, inventory.Count(onion, PrepSteps.None));
            Assert.AreEqual(1, inventory.Count(onion, PrepSteps.Peeled | PrepSteps.Sliced));
            Assert.AreEqual(4, inventory.Total(onion));

            Assert.IsTrue(inventory.Take(onion, PrepSteps.None, 2));
            Assert.AreEqual(1, inventory.Count(onion, PrepSteps.None));
        }

        [Test]
        public void Take_FailsWithoutChanges_WhenThereIsNotEnough()
        {
            Inventory inventory = new Inventory();
            IngredientDefinition rice = Ingredient("Rice");
            inventory.Add(rice, PrepSteps.None, 1);

            Assert.IsFalse(inventory.Take(rice, PrepSteps.None, 2));
            Assert.IsFalse(inventory.Take(rice, PrepSteps.Sliced));
            Assert.AreEqual(1, inventory.Count(rice, PrepSteps.None));
        }

        [Test]
        public void TakingTheLastUnit_RemovesTheEntry()
        {
            Inventory inventory = new Inventory();
            IngredientDefinition rice = Ingredient("Rice");
            inventory.Add(rice, PrepSteps.None, 1);

            inventory.Take(rice, PrepSteps.None);

            Assert.IsTrue(inventory.IsEmpty);
            Assert.AreEqual(0, inventory.Entries().Count);
        }

        [Test]
        public void Entries_AreSortedByNameThenPrep()
        {
            Inventory inventory = new Inventory();
            IngredientDefinition salt = Ingredient("Salt");
            IngredientDefinition onion = Ingredient("Onion");
            inventory.Add(salt, PrepSteps.None);
            inventory.Add(onion, PrepSteps.Sliced);
            inventory.Add(onion, PrepSteps.None);

            List<Inventory.Entry> entries = inventory.Entries();

            Assert.AreSame(onion, entries[0].Ingredient);
            Assert.AreEqual(PrepSteps.None, entries[0].Prep);
            Assert.AreEqual(PrepSteps.Sliced, entries[1].Prep);
            Assert.AreSame(salt, entries[2].Ingredient);
        }

        [Test]
        public void SpoilPrepped_RemovesPreppedStock_AndKeepsRaw()
        {
            Inventory inventory = new Inventory();
            IngredientDefinition potato = Ingredient("Potato");
            inventory.Add(potato, PrepSteps.None, 2);
            inventory.Add(potato, PrepSteps.Peeled, 3);

            List<Inventory.Entry> spoiled = inventory.SpoilPrepped();

            Assert.AreEqual(1, spoiled.Count);
            Assert.AreEqual(3, spoiled[0].Quantity);
            Assert.AreEqual(2, inventory.Count(potato, PrepSteps.None));
            Assert.AreEqual(0, inventory.Count(potato, PrepSteps.Peeled));
        }

        [Test]
        public void LateDish_IsStillPaid_ButNotTippedOrHappy()
        {
            IngredientDefinition sweet = Ingredient("Sweet", new FlavourProfile { sweet = 8f }, price: 2f);
            RecipeDefinition order = ScriptableObject.CreateInstance<RecipeDefinition>();
            created.Add(order);
            order.Configure("Sweet thing", "Test", new[] { new IngredientPortion(sweet, 1) });
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            created.Add(zone);
            zone.Configure("Sweet", new FlavourProfile { sweet = 6f }, FlavourProfile.Filled(10f));
            Customer customer = new Customer { secretDesire = zone };
            DishJudge.Settings settings = DishJudge.Settings.Default;
            settings.Margin = 2f;

            JudgeResult onTime = DishJudge.Judge(order, order.CreatePerfectPortions(), customer, new[] { zone }, null, settings);
            JudgeResult late = DishJudge.Judge(order, order.CreatePerfectPortions(), customer, new[] { zone }, null, settings, late: true);

            Assert.IsTrue(onTime.IsHappy);
            Assert.Greater(onTime.Tip, 0f);

            Assert.AreEqual(Verdict.DreamMeal, late.Verdict);
            Assert.AreEqual(4f, late.Payment, 0.0001f);
            Assert.AreEqual(0f, late.Tip);
            Assert.IsTrue(late.IsPaid);
            Assert.IsFalse(late.IsHappy);
        }
    }
}
