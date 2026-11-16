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

        private static JudgeResult Result(Verdict verdict)
        {
            bool paid = verdict == Verdict.Ordered || verdict == Verdict.VeryClose || verdict == Verdict.CloseEnough || verdict == Verdict.DreamMeal;
            return new JudgeResult { Verdict = verdict, Payment = paid ? 5f : 0f };
        }

        // ------------------------------------------------------------------ Stages

        [Test]
        public void StartsAStranger_AndBecomesFamiliarAfterEnoughLikedMeals()
        {
            Customer customer = new Customer { name = "The Man", likedMealsToFamiliar = 3 };
            CustomerRelationship relationship = new CustomerRelationship(customer);

            Assert.AreEqual(NpcStage.Stranger, relationship.Stage);

            relationship.Record(Result(Verdict.Ordered), new List<IngredientPortion>());
            RelationshipChange second = relationship.Record(Result(Verdict.VeryClose), new List<IngredientPortion>());
            Assert.IsFalse(second.StageChanged);

            RelationshipChange third = relationship.Record(Result(Verdict.Ordered), new List<IngredientPortion>());
            Assert.IsTrue(third.StageChanged);
            Assert.AreEqual(NpcStage.Familiar, third.After);
        }

        [Test]
        public void OnlyExactAndVeryClose_CountAsLiked()
        {
            Customer customer = new Customer { likedMealsToFamiliar = 1 };
            CustomerRelationship relationship = new CustomerRelationship(customer);

            relationship.Record(Result(Verdict.Disliked), new List<IngredientPortion>());
            relationship.Record(Result(Verdict.CloseEnough), new List<IngredientPortion>());
            relationship.Record(Result(Verdict.Refused), new List<IngredientPortion>());

            Assert.AreEqual(0, relationship.LikedCount);
            Assert.AreEqual(NpcStage.Stranger, relationship.Stage);
        }

        [Test]
        public void DreamMeal_MakesThemFriendly_AsANamelessDishYouCanName()
        {
            IngredientDefinition sugar = Ingredient("Sugar");
            RecipeDefinition usual = Recipe("Usual", new IngredientPortion(Ingredient("Rice"), 1, cooking: CookMethods.Fried));
            Customer customer = new Customer { name = "The Woman", orders = new List<RecipeDefinition> { usual } };
            CustomerRelationship relationship = new CustomerRelationship(customer);
            List<IngredientPortion> served = new List<IngredientPortion> { new IngredientPortion(sugar, 2, cooking: CookMethods.Boiled, doneness: Doneness.Perfect) };

            RelationshipChange change = relationship.Record(Result(Verdict.DreamMeal), served);
            created.Add(relationship.DreamMeal);

            Assert.AreEqual(NpcStage.Friendly, change.After);
            Assert.IsTrue(change.FoundDream);
            Assert.AreEqual(CustomerRelationship.Nameless, relationship.DreamMeal.DisplayName);
            Assert.AreEqual("Emphis", relationship.DreamMeal.Origin);

            CustomerRelationship.Rename(relationship.DreamMeal, "  Grandma's tang  ");
            Assert.AreEqual("Grandma's tang", relationship.DreamMeal.DisplayName);
            Assert.IsTrue(relationship.DreamMeal.Matches(served), "renaming keeps the recipe");

            RelationshipChange again = relationship.Record(Result(Verdict.DreamMeal), served);
            Assert.IsFalse(again.FoundDream, "the dream is only found once");

            System.Random random = new System.Random(1);
            bool orderedDream = false;
            for(int i = 0; i < 20; i++) orderedDream |= relationship.PickOrder(random, new[] { usual }) == relationship.DreamMeal;
            Assert.IsTrue(orderedDream, "they order their dream meal once found");
        }

        [Test]
        public void VeryCloseDish_IsAddedToWhatTheyOrder_Once()
        {
            RecipeDefinition usual = Recipe("Usual", new IngredientPortion(Ingredient("Rice"), 1, cooking: CookMethods.Fried));
            Customer customer = new Customer { name = "The Sleeper", orders = new List<RecipeDefinition> { usual } };
            CustomerRelationship relationship = new CustomerRelationship(customer);
            List<IngredientPortion> served = new List<IngredientPortion> { new IngredientPortion(Ingredient("Chilli"), 3, PrepSteps.Grated, CookMethods.Fried) };

            RelationshipChange first = relationship.Record(Result(Verdict.VeryClose), served);
            created.Add(first.NewLikedMeal);
            RelationshipChange again = relationship.Record(Result(Verdict.VeryClose), served);

            Assert.IsNotNull(first.NewLikedMeal);
            Assert.AreEqual(CustomerRelationship.Nameless, first.NewLikedMeal.DisplayName);
            Assert.IsNull(again.NewLikedMeal, "the same dish isn't added twice");
            Assert.AreEqual(2, relationship.LikedMeals.Count);
        }

        [Test]
        public void VeryCloseDish_ThatIsAKnownRecipe_IsLikedAsThatRecipe()
        {
            RecipeDefinition usual = Recipe("Usual", new IngredientPortion(Ingredient("Rice"), 1, cooking: CookMethods.Fried));
            IngredientPortion chilli = new IngredientPortion(Ingredient("Chilli"), 3, PrepSteps.Grated, CookMethods.Fried);
            RecipeDefinition hot = Recipe("Hot one", chilli);
            CustomerRelationship relationship = new CustomerRelationship(new Customer { orders = new List<RecipeDefinition> { usual } });

            RelationshipChange change = relationship.Record(Result(Verdict.VeryClose), new List<IngredientPortion> { chilli }, new[] { usual, hot });

            Assert.AreSame(hot, change.NewLikedMeal);
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
            Assert.AreEqual(DialogueEventType.CorrectOrder, CustomerDialogue.EventFor(Result(Verdict.Ordered)));
            Assert.AreEqual(DialogueEventType.WrongOrderTasty, CustomerDialogue.EventFor(Result(Verdict.VeryClose)));
            Assert.AreEqual(DialogueEventType.WrongOrderTasty, CustomerDialogue.EventFor(Result(Verdict.CloseEnough)));
            Assert.AreEqual(DialogueEventType.WrongOrderNasty, CustomerDialogue.EventFor(Result(Verdict.Disliked)));
            Assert.AreEqual(DialogueEventType.WrongOrderNasty, CustomerDialogue.EventFor(Result(Verdict.Refused)));
            Assert.AreEqual(DialogueEventType.DreamOrder, CustomerDialogue.EventFor(Result(Verdict.DreamMeal)));
            Assert.AreEqual(DialogueEventType.CorrectOrder, CustomerDialogue.EventFor(Result(Verdict.DreamMeal), firstDream: false));
        }

        [Test]
        public void OrderDialogue_ReadsPayloadThenTokens()
        {
            Assert.AreEqual(CustomerDialogue.OrderKind.Named, CustomerDialogue.ReadOrder("MSBeef fried rice", "Whip me up an MSBeef rice", out string dish));
            Assert.AreEqual("MSBeef fried rice", dish);
            Assert.AreEqual(CustomerDialogue.OrderKind.Dream, CustomerDialogue.ReadOrder("[dream dish]", "Hey Pot Boy.", out _));
            Assert.AreEqual(CustomerDialogue.OrderKind.Liked, CustomerDialogue.ReadOrder("", "Need that [liked meal], Pot Boy.", out _));
            Assert.AreEqual(CustomerDialogue.OrderKind.Liked, CustomerDialogue.ReadOrder(null, "Hit me with that [Liked Dish], boss!", out _));
            Assert.AreEqual(CustomerDialogue.OrderKind.Default, CustomerDialogue.ReadOrder(null, "The food on this station...", out _));
        }

        [Test]
        public void Tokens_UseTheDishName_OrAFallbackWhenUnnamed()
        {
            Assert.AreEqual("Need that Calorice, Pot Boy.", CustomerDialogue.FillTokens("Need that [liked meal], Pot Boy.", "Calorice", null));
            Assert.AreEqual("Hit me with that the thing you made me the other day, boss!",
                CustomerDialogue.FillTokens("Hit me with that [liked dish], boss!", null, null));
            Assert.AreEqual("Get me that the regular, will ya?", CustomerDialogue.FillTokens("Get me that [dream dish], will ya?", null, null));
            Assert.AreEqual("Ohh, [name of her lover].", CustomerDialogue.FillTokens("Ohh, [name of her lover].", "x", "y"), "other tokens stay");
        }

        [Test]
        public void Catalog_PlaysOptionsInOrder_ThenRandomly_AndOneShotsOnce()
        {
            DialogueGraph o1 = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            DialogueGraph o2 = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            DialogueGraph o3 = Track(ScriptableObject.CreateInstance<DialogueGraph>());
            o2.isOneShot = true;
            DialogueCatalog catalog = Track(ScriptableObject.CreateInstance<DialogueCatalog>());
            catalog.groups.Add(new DialogueNpcGroup
            {
                npcId = DialogueNpcId.Woman,
                entries = new List<DialogueEntry>
                {
                    new DialogueEntry { graph = o1, eventType = DialogueEventType.Musing, npcState = DialogueNpcState.Friendly },
                    new DialogueEntry { graph = o2, eventType = DialogueEventType.Musing, npcState = DialogueNpcState.Friendly },
                    new DialogueEntry { graph = o3, eventType = DialogueEventType.Musing, npcState = DialogueNpcState.Friendly },
                },
            });

            DialogueGraph Pick() => catalog.SelectGraph(DialogueNpcId.Woman, DialogueEventType.Musing, DialogueNpcState.Friendly);

            Assert.AreSame(o1, Pick());
            Assert.AreSame(o2, Pick());
            Assert.AreSame(o3, Pick());

            for(int i = 0; i < 30; i++) Assert.AreNotSame(o2, Pick(), "a one-shot never plays again");

            catalog.ResetPlayed();
            Assert.AreSame(o1, Pick(), "a new game starts over in order");
            Assert.AreSame(o2, Pick());
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
                    new DialogueEntry { graph = graph, eventType = DialogueEventType.Order, npcState = DialogueNpcState.Familiar },
                    new DialogueEntry { graph = graph, eventType = DialogueEventType.Order, npcState = DialogueNpcState.None },
                },
            });

            Assert.IsTrue(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Order, NpcStage.Familiar, out DialogueNpcState warm));
            Assert.AreEqual(DialogueNpcState.Familiar, warm);

            Assert.IsTrue(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Order, NpcStage.Stranger, out DialogueNpcState any));
            Assert.AreEqual(DialogueNpcState.None, any);

            Assert.IsFalse(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Woman, DialogueEventType.Hurry, NpcStage.Stranger, out _));
            Assert.IsFalse(CustomerDialogue.TryResolve(catalog, DialogueNpcId.Man, DialogueEventType.Order, NpcStage.Stranger, out _));
        }
    }
}
