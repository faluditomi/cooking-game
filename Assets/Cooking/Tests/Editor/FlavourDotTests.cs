using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class FlavourDotTests
    {
        private static FlavourZone Wedge(string name, float axis, float spread, float inner, float outer)
        {
            FlavourZone zone = ScriptableObject.CreateInstance<FlavourZone>();
            zone.Configure(name, FlavourProfile.Zero, FlavourProfile.Filled(FlavourProfile.MaxValue));
            zone.ConfigureChartShape(axis, spread, inner, outer);
            return zone;
        }

        [Test]
        public void Dot_PointsTowardsWhatStandsOut()
        {
            Vector2 salty = FlavourDot.Of(new FlavourProfile { salty = 8f, nutrition = 2f, texture = 2f });
            Vector2 painful = FlavourDot.Of(new FlavourProfile { pain = 8f, nutrition = 2f, texture = 2f });

            Assert.AreEqual(1f, FlavourDot.AxisPosition(salty), 0.35f, "salty is axis 1");
            Assert.AreEqual(5f, FlavourDot.AxisPosition(painful), 0.35f, "pain is axis 5");
        }

        [Test]
        public void Dot_OfABalancedDish_IsInTheMiddle()
        {
            Vector2 dot = FlavourDot.Of(FlavourProfile.Filled(4f));
            Assert.Less(dot.magnitude, 0.01f);
        }

        [Test]
        public void Zone_WithAChartShape_JudgesByTheDot()
        {
            FlavourZone saltyWedge = Wedge("Salty", 1f, 0.6f, 0.1f, 0.9f);

            try
            {
                Assert.AreEqual(1f, FlavourEvaluator.Score(new FlavourProfile { salty = 8f, nutrition = 2f }, saltyWedge), 0.0001f);
                Assert.AreEqual(0f, FlavourEvaluator.Score(new FlavourProfile { pain = 8f, nutrition = 2f }, saltyWedge), 0.0001f);
                Assert.Less(FlavourEvaluator.Score(FlavourProfile.Filled(4f), saltyWedge), 1f, "the middle is outside its inner edge");
            }
            finally
            {
                Object.DestroyImmediate(saltyWedge);
            }
        }

        private static DishJudge.Settings OneIngredient
        {
            get
            {
                DishJudge.Settings settings = DishJudge.Settings.Default;
                settings.MinIngredients = 1;
                return settings;
            }
        }

        [Test]
        public void Judge_PaysForADish_WhoseDotIsInATastyWedge()
        {
            IngredientDefinition salt = ScriptableObject.CreateInstance<IngredientDefinition>();
            salt.Configure("Salt", new FlavourProfile { salty = 8f, nutrition = 2f });
            IngredientDefinition chilli = ScriptableObject.CreateInstance<IngredientDefinition>();
            chilli.Configure("Chilli", new FlavourProfile { pain = 8f, nutrition = 2f });
            RecipeDefinition order = ScriptableObject.CreateInstance<RecipeDefinition>();
            order.Configure("Order", "Test", new[] { new IngredientPortion(chilli, 1, cooking: CookMethods.Boiled) });
            FlavourZone saltyWedge = Wedge("Salty", 1f, 0.6f, 0.1f, 0.9f);

            try
            {
                JudgeResult tasty = DishJudge.Judge(order, new[] { new IngredientPortion(salt, 1, cooking: CookMethods.Boiled) }, new Customer(),
                    new[] { saltyWedge }, null, OneIngredient);
                Assert.IsTrue(tasty.SameZone);
                Assert.IsTrue(tasty.IsPaid);

                FlavourZone painWedge = Wedge("Pain", 5f, 0.6f, 0.1f, 0.9f);
                JudgeResult notTasty = DishJudge.Judge(order, new[] { new IngredientPortion(salt, 1, cooking: CookMethods.Boiled) }, new Customer(),
                    new[] { painWedge }, null, OneIngredient);
                Assert.IsFalse(notTasty.IsPaid);
                Object.DestroyImmediate(painWedge);
            }
            finally
            {
                Object.DestroyImmediate(salt);
                Object.DestroyImmediate(chilli);
                Object.DestroyImmediate(order);
                Object.DestroyImmediate(saltyWedge);
            }
        }
    }
}
