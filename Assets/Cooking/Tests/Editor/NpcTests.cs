using System.Collections.Generic;
using Cooking.Prototype;
using DialogueSystem;
using NUnit.Framework;
using UnityEngine;

namespace Cooking.Tests
{
    public class NpcTests
    {
        private readonly List<Object> created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach(Object obj in created) Object.DestroyImmediate(obj);
            created.Clear();
        }

        private T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        private IngredientDefinition Ingredient(string name)
        {
            IngredientDefinition ingredient = Track(ScriptableObject.CreateInstance<IngredientDefinition>());
            ingredient.Configure(name, FlavourProfile.Zero);
            return ingredient;
        }

        private RecipeDefinition Recipe(string name, params IngredientPortion[] portions)
        {
            RecipeDefinition recipe = Track(ScriptableObject.CreateInstance<RecipeDefinition>());
            recipe.Configure(name, "Test", portions);
            return recipe;
        }

        private static JudgeResult Result(Verdict verdict, bool late = false)
        {
            bool paid = verdict == Verdict.Ordered || verdict == Verdict.LikedSubstitute || verdict == Verdict.DreamMeal;
            return new JudgeResult { Verdict = verdict, Payment = paid ? 5f : 0f, Late = late };
        }

        // ------------------------------------------------------------------ Stages

        [Test]
        public void StartsDejected_AndWarmsUpAfterEnoughHappyMeals()
        {
            Customer customer = new Customer { name = "The Man", happyMealsToWarmUp = 2 };
            CustomerRelationship relationship = new CustomerRelationship(customer);

            Assert.AreEqual(NpcStage.Dejected, relationship.Stage);

            RelationshipChange first = relationship.Record(Result(Verdict.Ordered), new List<IngredientPortion>());
            Assert.IsFalse(first.StageChanged);

            RelationshipChange second = relationship.Record(Result(Verdict.Ordered), new List<IngredientPortion>());
            Assert.IsTrue(second.StageChanged);
            Assert.AreEqual(NpcStage.WarmedUp, second.After);
        }

        [Test]
        public void UnhappyMeals_DontCountTowardsWarmingUp()
        {
            Customer customer = new Customer { happyMealsToWarmUp = 1 };
            CustomerRelationship relationship = new CustomerRelationship(customer);

            relationship.Record(Result(Verdict.Disliked), new List<IngredientPortion>());
            relationship.Record(Result(Verdict.Ordered, late: true), new List<IngredientPortion>());
            relationship.Record(Result(Verdict.Refused), new List<IngredientPortion>());

            Assert.AreEqual(0, relationship.HappyMeals);
            Assert.AreEqual(NpcStage.Dejected, relationship.Stage);
        }

        [Test]
        public void DreamMeal_IsEureka_AndTheyAlwaysOrderItAfter()
        {
            IngredientDefinition sugar = Ingredient("Sugar");
            RecipeDefinition usual = Recipe("Usual", new IngredientPortion(Ingredient("Rice"), 1, cooking: CookMethods.Fried));
            Customer customer = new Customer { name = "The Woman", orders = new List<RecipeDefinition> { usual } };
            CustomerRelationship relationship = new CustomerRelationship(customer);
            List<IngredientPortion> served = new List<IngredientPortion> { new IngredientPortion(sugar, 2, cooking: CookMethods.Boiled, doneness: Doneness.Perfect) };

            RelationshipChange change = relationship.Record(Result(Verdict.DreamMeal, late: true), served);
            created.Add(relationship.DreamMeal);

            Assert.AreEqual(NpcStage.Eureka, change.After, "even served late, the dream meal is the eureka moment");
            Assert.IsNotNull(relationship.DreamMeal);
            Assert.AreEqual("Emphis", relationship.DreamMeal.Origin);
            Assert.IsTrue(relationship.DreamMeal.Matches(served));

            System.Random random = new System.Random(1);
            for(int i = 0; i < 5; i++) Assert.AreSame(relationship.DreamMeal, relationship.PickOrder(random, new[] { usual }));
        }

        [Test]
        public void LikedSubstitute_IsAddedToWhatTheyOrder_Once()
        {
            RecipeDefinition usual = Recipe("Usual", new IngredientPortion(Ingredient("Rice"), 1, cooking: CookMethods.Fried));
            Customer customer = new Customer { name = "The Sleeper", orders = new List<RecipeDefinition> { usual } };
            CustomerRelationship relationship = new CustomerRelationship(customer);
            List<IngredientPortion> served = new List<IngredientPortion> { new IngredientPortion(Ingredient("Chilli"), 3, PrepSteps.Grated, CookMethods.Fried) };

            RelationshipChange first = relationship.Record(Result(Verdict.LikedSubstitute), served);
            created.Add(first.NewLikedMeal);
            RelationshipChange again = relationship.Record(Result(Verdict.LikedSubstitute), served);

            Assert.IsNotNull(first.NewLikedMeal);
            Assert.AreEqual("Emphis's special for The Sleeper", first.NewLikedMeal.DisplayName);
            Assert.IsNull(again.NewLikedMeal, "the same dish isn't added twice");
            Assert.AreEqual(2, relationship.LikedMeals.Count);
        }

        [Test]
        public void PickOrder_FallsBackToMenu_WhenTheyLikeNothingYet()
        {
            RecipeDefinition menuDish = Recipe("Menu dish", new IngredientPortion(Ingredient("Rice"), 1));
            CustomerRelationship relationship = new CustomerRelationship(new Customer());

            Assert.AreSame(menuDish, relationship.PickOrder(new System.Random(3), new[] { menuDish }));
        }

        // ------------------------------------------------------------------ Dialogue mapping

        [Test]
        public void DishOutcome_MapsToDialogueEvent()
        {
            Assert.AreEqual(DialogueEventType.GoodDish, CustomerDialogue.EventFor(Result(Verdict.Ordered)));
            Assert.AreEqual(DialogueEventType.GoodDish, CustomerDialogue.EventFor(Result(Verdict.LikedSubstitute)));
            Assert.AreEqual(DialogueEventType.BadDish, CustomerDialogue.EventFor(Result(Verdict.Disliked)));
            Assert.AreEqual(DialogueEventType.Refused, CustomerDialogue.EventFor(Result(Verdict.Refused)));
            Assert.AreEqual(DialogueEventType.LateDish, CustomerDialogue.EventFor(Result(Verdict.Ordered, late: true)));
            Assert.AreEqual(DialogueEventType.DreamMeal, CustomerDialogue.EventFor(Result(Verdict.DreamMeal, late: true)));
        }

        [Test]
        public void TryResolve_PrefersStageLines_ThenFallsBackToAnyStage()
        {
            DialogueGraph graph = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            DialogueCatalog catalog = Track(ScriptableObject.CreateInstance<DialogueCatalog>());
            catalog.groups.Add(new DialogueNpcGroup
            {
                npcId = DialogueNpcId.Woman,
                entries = new List<DialogueEntry>
                {
                    new DialogueEntry { graph = graph, eventType = DialogueEventType.Order, npcState = DialogueNpcState.WarmedUp },
                    new DialogueEntry { graph = graph, eventType = DialogueEventType.Order, npcState = DialogueNpcState.None },
                },
            });

            Assert.IsTrue(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Order, NpcStage.WarmedUp, out DialogueNpcState warm));
            Assert.AreEqual(DialogueNpcState.WarmedUp, warm);

            Assert.IsTrue(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Order, NpcStage.Dejected, out DialogueNpcState any));
            Assert.AreEqual(DialogueNpcState.None, any);

            Assert.IsFalse(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Hurry, NpcStage.Dejected, out _));
            Assert.IsFalse(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Man, DialogueEventType.Order, NpcStage.Dejected, out _));
        }
    }
}
