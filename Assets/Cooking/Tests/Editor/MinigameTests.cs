using System.Collections.Generic;
using Cooking.Prototype;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class MinigameTests
    {
        /// <summary>Feeds a held-pointer path to the gesture in small steps, like real mouse movement.</summary>
        private static int Drag(Gesture gesture, params Vector2[] path)
        {
            int strokes = 0;
            gesture.Begin(path[0]);

            for(int i = 1; i < path.Length; i++)
            {
                Vector2 from = path[i - 1];
                Vector2 to = path[i];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(from, to) / 5f));

                for(int s = 1; s <= steps; s++)
                {
                    if(gesture.Move(Vector2.Lerp(from, to, (float)s / steps))) strokes++;
                }
            }

            gesture.End();
            return strokes;
        }

        private static Vector2[] Arc(float radius, float fromDegrees, float toDegrees, int points = 24)
        {
            Vector2[] path = new Vector2[points + 1];

            for(int i = 0; i <= points; i++)
            {
                float a = Mathf.Lerp(fromDegrees, toDegrees, (float)i / points) * Mathf.Deg2Rad;
                path[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
            }

            return path;
        }

        // ------------------------------------------------------------------ Slicing / shaking

        [Test]
        public void Slice_CountsEachChangeOfDirection()
        {
            BackAndForthGesture slice = new BackAndForthGesture(vertical: true, requiredStrokes: 10, minTravel: 60f);

            int strokes = Drag(slice, new Vector2(0, 0), new Vector2(0, 80), new Vector2(0, -20), new Vector2(0, 70));

            Assert.AreEqual(3, strokes);
        }

        [Test]
        public void Slice_IgnoresShortWigglesAndSidewaysMovement()
        {
            BackAndForthGesture slice = new BackAndForthGesture(vertical: true, requiredStrokes: 10, minTravel: 60f);

            int strokes = Drag(slice, new Vector2(0, 0), new Vector2(0, 30), new Vector2(0, 0), new Vector2(200, 10), new Vector2(-200, 0));

            Assert.AreEqual(0, strokes);
        }

        [Test]
        public void Gesture_CompletesAtRequiredStrokes_AndStopsCounting()
        {
            BackAndForthGesture slice = new BackAndForthGesture(vertical: true, requiredStrokes: 2, minTravel: 60f);

            int strokes = Drag(slice, new Vector2(0, 0), new Vector2(0, 80), new Vector2(0, -20), new Vector2(0, 70), new Vector2(0, -30));

            Assert.AreEqual(2, strokes);
            Assert.IsTrue(slice.IsComplete);
            Assert.AreEqual(1f, slice.Progress);
        }

        [Test]
        public void Shake_IsHorizontal()
        {
            BackAndForthGesture shake = new BackAndForthGesture(vertical: false, requiredStrokes: 100, minTravel: 45f);

            Assert.AreEqual(0, Drag(shake, new Vector2(0, -60), new Vector2(0, 60), new Vector2(0, -60)), "vertical does nothing");
            Assert.AreEqual(3, Drag(shake, new Vector2(-30, 0), new Vector2(30, 0), new Vector2(-30, 0), new Vector2(30, 0)));
        }

        // ------------------------------------------------------------------ Grating

        [Test]
        public void Grate_CountsLeftToRightOnly()
        {
            SwipeRightGesture grate = new SwipeRightGesture(requiredStrokes: 10, minTravel: 100f);

            Assert.AreEqual(0, Drag(grate, new Vector2(150, 0), new Vector2(-150, 0)), "right to left");
            Assert.AreEqual(1, Drag(grate, new Vector2(-150, 0), new Vector2(-30, 0)), "one swipe right");
        }

        [Test]
        public void Grate_TurningBackDropsThePartialStroke()
        {
            SwipeRightGesture grate = new SwipeRightGesture(requiredStrokes: 10, minTravel: 100f);

            // 80 right, back 50, then 90 right: neither leg reaches 100 on its own.
            int strokes = Drag(grate, new Vector2(0, 0), new Vector2(80, 0), new Vector2(30, 0), new Vector2(120, 0));

            Assert.AreEqual(0, strokes);
        }

        [Test]
        public void Grate_RepeatedSwipesWithReturns()
        {
            SwipeRightGesture grate = new SwipeRightGesture(requiredStrokes: 10, minTravel: 100f);

            int strokes = Drag(grate, new Vector2(-120, 0), new Vector2(0, 0), new Vector2(-120, 5), new Vector2(0, 0), new Vector2(-120, -5), new Vector2(0, 0));

            Assert.AreEqual(3, strokes);
        }

        // ------------------------------------------------------------------ Peeling

        [Test]
        public void Peel_HalfCircleFromTopToBottom_OnEitherSide()
        {
            HalfCircleDownGesture right = new HalfCircleDownGesture(requiredStrokes: 10);
            HalfCircleDownGesture left = new HalfCircleDownGesture(requiredStrokes: 10);

            Assert.AreEqual(1, Drag(right, Arc(100f, 90f, -90f)), "down the right side");
            Assert.AreEqual(1, Drag(left, Arc(100f, 90f, 270f)), "down the left side");
        }

        [Test]
        public void Peel_BottomToTop_DoesNotCount()
        {
            HalfCircleDownGesture peel = new HalfCircleDownGesture(requiredStrokes: 10);

            Assert.AreEqual(0, Drag(peel, Arc(100f, -90f, 90f)));
        }

        [Test]
        public void Peel_StraightLineDown_DoesNotCount()
        {
            HalfCircleDownGesture peel = new HalfCircleDownGesture(requiredStrokes: 10);

            // Straight through the middle: too close to the centre and no sweep.
            Assert.AreEqual(0, Drag(peel, new Vector2(0, 100), new Vector2(0, -100)));
        }

        [Test]
        public void Peel_ReversingHalfwayBreaksTheStroke()
        {
            HalfCircleDownGesture peel = new HalfCircleDownGesture(requiredStrokes: 10);
            // Down to the side, back up a little (not to the top), then on to the bottom.
            List<Vector2> path = new List<Vector2>(Arc(100f, 90f, 0f));
            path.AddRange(Arc(100f, 0f, 20f));
            path.AddRange(Arc(100f, 20f, -90f));

            Assert.AreEqual(0, Drag(peel, path.ToArray()));
        }

        [Test]
        public void Peel_BackAndForthAcrossTheBottomCountsEachDownwardArc()
        {
            HalfCircleDownGesture peel = new HalfCircleDownGesture(requiredStrokes: 10);
            List<Vector2> path = new List<Vector2>(Arc(100f, 90f, -90f));
            path.AddRange(Arc(100f, -90f, -270f)); // up the left side: bottom to top, no credit
            path.AddRange(Arc(100f, 90f, -90f));   // down the right again

            Assert.AreEqual(2, Drag(peel, path.ToArray()));
        }

        // ------------------------------------------------------------------ Pot rule

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

        [Test]
        public void Pot_WithoutStock_TakesOneIngredientAtATime()
        {
            IngredientDefinition potato = Ingredient("Potato");
            IngredientDefinition salt = Ingredient("Salt");
            CookingStation pot = CookingStation.CreatePot();

            Assert.IsTrue(pot.Add(new IngredientPortion(potato, 1, PrepSteps.Peeled)));
            Assert.IsTrue(pot.Add(new IngredientPortion(potato, 1)), "more of the same ingredient is fine");
            Assert.IsFalse(pot.CanAdd(new IngredientPortion(salt, 1), out string reason));
            StringAssert.Contains("stock", reason);
        }

        [Test]
        public void Pot_WithStock_BecomesSoupAndTakesAnything()
        {
            IngredientDefinition cube = Ingredient("Soup Cube", stock: true);
            IngredientDefinition potato = Ingredient("Potato");
            IngredientDefinition salt = Ingredient("Salt");
            CookingStation pot = CookingStation.CreatePot();

            Assert.IsTrue(pot.Add(new IngredientPortion(potato, 1)));
            Assert.IsTrue(pot.Add(new IngredientPortion(cube, 1)), "stock can join a single ingredient");
            Assert.IsTrue(pot.IsSoup);
            Assert.IsTrue(pot.Add(new IngredientPortion(salt, 2)));
            Assert.AreEqual(3, pot.Contents.Count);
        }

        [Test]
        public void Wok_TakesAnyMix()
        {
            CookingStation wok = CookingStation.CreateWok();

            Assert.IsTrue(wok.Add(new IngredientPortion(Ingredient("Rice"), 1)));
            Assert.IsTrue(wok.Add(new IngredientPortion(Ingredient("Salt"), 1)));
        }
    }
}
