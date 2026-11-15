using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class ShoppingCartTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private IngredientDefinition Ingredient(string name, float price)
        {
            IngredientDefinition ingredient = ScriptableObject.CreateInstance<IngredientDefinition>();
            ingredient.Configure(name, FlavourProfile.Zero, newPrice: price);
            created.Add(ingredient);
            return ingredient;
        }

        private RecipeDefinition Recipe(params IngredientPortion[] portions)
        {
            RecipeDefinition recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.Configure("Recipe", "Test", portions);
            created.Add(recipe);
            return recipe;
        }

        [Test]
        public void AddAndRemove_KeepCountsAndTotal()
        {
            ShoppingCart cart = new ShoppingCart();
            IngredientDefinition salt = Ingredient("Salt", 0.5f);
            IngredientDefinition rice = Ingredient("Rice", 1f);

            cart.Add(salt, 3);
            cart.Add(rice);
            cart.Remove(salt);

            Assert.AreEqual(2, cart.Count(salt));
            Assert.AreEqual(3, cart.TotalUnits);
            Assert.AreEqual(2f, cart.TotalCost, 0.0001f);

            cart.Remove(rice, 5);
            Assert.AreEqual(1, cart.Lines().Count);
        }

        [Test]
        public void AddRecipe_AddsEveryIngredientPerServing()
        {
            ShoppingCart cart = new ShoppingCart();
            IngredientDefinition onion = Ingredient("Onion", 1f);
            IngredientDefinition salt = Ingredient("Salt", 0.5f);
            // Two portions of onion with different prep still mean two raw onions to buy.
            RecipeDefinition recipe = Recipe(new IngredientPortion(onion, 1, PrepSteps.Peeled), new IngredientPortion(onion, 1), new IngredientPortion(salt, 2));

            int added = cart.AddRecipe(recipe, servings: 2);

            Assert.AreEqual(8, added);
            Assert.AreEqual(4, cart.Count(onion));
            Assert.AreEqual(4, cart.Count(salt));
        }

        [Test]
        public void AddMissing_CountsStockAndCart()
        {
            ShoppingCart cart = new ShoppingCart();
            Inventory stock = new Inventory();
            IngredientDefinition potato = Ingredient("Potato", 2f);
            IngredientDefinition salt = Ingredient("Salt", 0.5f);
            IngredientDefinition cube = Ingredient("Soup Cube", 2f);
            RecipeDefinition recipe = Recipe(new IngredientPortion(cube, 1), new IngredientPortion(potato, 1, PrepSteps.Peeled), new IngredientPortion(salt, 2));

            stock.Add(potato, PrepSteps.Peeled, 1); // prepped stock counts
            stock.Add(salt, PrepSteps.None, 1);
            cart.Add(cube, 1);

            int added = cart.AddMissing(recipe, stock);

            Assert.AreEqual(1, added, "only one salt is missing");
            Assert.AreEqual(1, cart.Count(salt));
            Assert.AreEqual(0, cart.Count(potato));
            Assert.AreEqual(0, cart.AddMissing(recipe, stock), "covered now");
        }

        [Test]
        public void Checkout_PaysAndStocksRaw()
        {
            ShoppingCart cart = new ShoppingCart();
            Inventory stock = new Inventory();
            IngredientDefinition rice = Ingredient("Rice", 1.5f);
            cart.Add(rice, 2);
            float money = 10f;

            Assert.IsTrue(cart.Checkout(stock, ref money));

            Assert.AreEqual(7f, money, 0.0001f);
            Assert.AreEqual(2, stock.Count(rice, PrepSteps.None));
            Assert.IsTrue(cart.IsEmpty);
        }

        [Test]
        public void Checkout_FailsWithoutChanges_WhenYouCantAffordIt()
        {
            ShoppingCart cart = new ShoppingCart();
            Inventory stock = new Inventory();
            IngredientDefinition beef = Ingredient("Beef", 15f);
            cart.Add(beef);
            float money = 10f;

            Assert.IsFalse(cart.Checkout(stock, ref money));

            Assert.AreEqual(10f, money);
            Assert.IsTrue(stock.IsEmpty);
            Assert.AreEqual(1, cart.Count(beef));
        }
    }
}
