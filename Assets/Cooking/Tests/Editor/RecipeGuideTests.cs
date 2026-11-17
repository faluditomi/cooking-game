using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class RecipeGuideTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private IngredientDefinition Ingredient(string name, bool stock = false)
        {
            IngredientDefinition ingredient = ScriptableObject.CreateInstance<IngredientDefinition>();
            ingredient.Configure(name, FlavourProfile.Zero, newIsStock: stock);
            created.Add(ingredient);
            return ingredient;
        }

        private RecipeDefinition Recipe(params IngredientPortion[] portions)
        {
            RecipeDefinition recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
            recipe.Configure("Dish", "Test", portions);
            created.Add(recipe);
            return recipe;
        }

        [Test]
        public void PrepSteps_NameTheToolsInOrder()
        {
            Assert.AreEqual("peel <color=#a6a199>(peeler)</color>, then slice <color=#a6a199>(knife)</color>",
                RecipeGuide.DescribePrep(PrepSteps.Sliced | PrepSteps.Peeled));
        }

        [Test]
        public void FriedDish_ListsBuyPrepAndPan()
        {
            IngredientDefinition onion = Ingredient("Onion");
            IngredientDefinition rice = Ingredient("Rice");
            RecipeDefinition recipe = Recipe(new IngredientPortion(rice, 1, cooking: CookMethods.Fried),
                new IngredientPortion(onion, 2, PrepSteps.Peeled | PrepSteps.Sliced, CookMethods.Fried));

            string steps = RecipeGuide.Steps(recipe, ingredient => ingredient == rice ? 1 : 0);

            StringAssert.Contains("2× Onion", steps);
            StringAssert.Contains("have 1", steps);
            StringAssert.Contains("As they are: Rice", steps);
            StringAssert.Contains("<b>Pan</b>", steps);
            StringAssert.DoesNotContain("<b>Pot</b>", steps);
        }

        [Test]
        public void Soup_SaysTheCubeMakesItASoup()
        {
            IngredientDefinition cube = Ingredient("Soup Cube", stock: true);
            IngredientDefinition potato = Ingredient("Potato");
            RecipeDefinition recipe = Recipe(new IngredientPortion(cube, 1, cooking: CookMethods.Boiled),
                new IngredientPortion(potato, 1, PrepSteps.Peeled, CookMethods.Boiled));

            string steps = RecipeGuide.Steps(recipe);

            StringAssert.Contains("<b>Pot</b>", steps);
            StringAssert.Contains("Soup Cube makes it a soup", steps);
        }

        [Test]
        public void PotWithoutStock_WarnsAboutOneAtATime()
        {
            RecipeDefinition recipe = Recipe(new IngredientPortion(Ingredient("Potato"), 1, cooking: CookMethods.Boiled),
                new IngredientPortion(Ingredient("Salt"), 1, cooking: CookMethods.Boiled));

            StringAssert.Contains("one ingredient at a time", RecipeGuide.Steps(recipe));
        }

        [Test]
        public void RawPortions_GoStraightOnThePlate()
        {
            RecipeDefinition recipe = Recipe(new IngredientPortion(Ingredient("Pak Choi"), 1));

            string steps = RecipeGuide.Steps(recipe);

            StringAssert.Contains("<b>Raw</b>", steps);
            StringAssert.Contains("Nothing to prep.", steps);
        }
    }
}
