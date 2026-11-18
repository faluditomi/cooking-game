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
        public void DreamMeal_NeedsItsIngredient_AndBeatsARefusal()
        {
            IngredientDefinition sweet = Ingredient("Sweet", new FlavourProfile { sweet = 8f }, price: 2f);
            IngredientDefinition beef = Ingredient("Beef", FlavourProfile.Zero, price: 1f);
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            created.Add(zone);
            zone.Configure("Sweet", new FlavourProfile { sweet = 3f }, FlavourProfile.Filled(10f));
            Customer customer = new Customer
            {
                secretDesire = zone,
                dreamNeeds = new List<IngredientDefinition> { beef },
                refuses = new List<IngredientDefinition> { beef },
            };

            RecipeDefinition order = ScriptableObject.CreateInstance<RecipeDefinition>();
            created.Add(order);
            order.Configure("Sweet thing", "Test", new[] { new IngredientPortion(sweet, 1) });
            DishJudge.Settings settings = DishJudge.Settings.Default;
            settings.Margin = 2f;
            // Two-ingredient dishes, cooked, are enough to count here.
            settings.MinIngredients = 2;
            settings.MinPrepped = 0;

            JudgeResult withoutBeef = DishJudge.Judge(order, new[] { new IngredientPortion(sweet, 1) }, customer, new[] { zone }, null, settings);
            JudgeResult withBeef = DishJudge.Judge(order, new[] { new IngredientPortion(sweet, 1, cooking: CookMethods.Boiled), new IngredientPortion(beef, 1, cooking: CookMethods.Boiled) },
                customer, new[] { zone }, null, settings);
            JudgeResult beefOnly = DishJudge.Judge(order, new[] { new IngredientPortion(beef, 1) }, customer, new[] { zone }, null, settings);

            Assert.AreEqual(Verdict.Ordered, withoutBeef.Verdict, "in the zone but without beef it's just their order");
            Assert.AreEqual(Verdict.DreamMeal, withBeef.Verdict, "the dream meal wins over refusing beef");
            Assert.Greater(withBeef.Tip, 0f);
            Assert.IsTrue(withBeef.IsLiked);
            Assert.AreEqual(Verdict.Refused, beefOnly.Verdict, "outside the dream they still refuse beef");
        }
    }
}
