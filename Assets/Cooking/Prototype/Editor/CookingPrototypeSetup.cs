using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Builds the cooking prototype scene and the design-doc recipe assets.
    /// Existing recipe assets are kept as they are, so tuning them by hand is safe.
    /// </summary>
    public static class CookingPrototypeSetup
    {
        private const string DataRoot = "Assets/Cooking/Data";
        private const string RecipeFolder = DataRoot + "/Recipes";
        private const string ScenePath = "Assets/Cooking/Prototype/CookingPrototype.unity";

        private static readonly string[] PantryOrder =
        {
            "Soup Cube", "Mushroom", "Potato", "Synthetic Chicken", "Real Beef", "Tofu", "Onion", "Chilli", "Salt",
            "Sugar", "Pepper", "MSG2 Cube", "Rice", "Seaweed Noodles", "Citric Acid", "Pak Choi", "Beef",
        };

        [MenuItem("Custom Tools/Cooking Prototype/Build Scene")]
        public static void BuildScene()
        {
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Dictionary<string, RecipeDefinition> recipes = EnsureRecipes();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = UIFactory.Background;
            camera.orthographic = true;

            GameObject eventSystem = new GameObject("EventSystem", typeof(EventSystem));
            eventSystem.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();

            GameObject root = new GameObject("Cooking Prototype");
            CookingPrototype prototype = root.AddComponent<CookingPrototype>();
            prototype.Setup(LoadRules(), LoadZones(), LoadPantry(), recipes.Values, CreateCustomers(recipes));
            EditorUtility.SetDirty(prototype);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("[Cooking] Built " + ScenePath + ". Press Play to cook.");
        }

        [MenuItem("Custom Tools/Cooking Prototype/Open Scene")]
        public static void OpenScene()
        {
            if(!File.Exists(ScenePath))
            {
                BuildScene();
                return;
            }

            if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        private static FlavourRules LoadRules()
        {
            return AssetDatabase.LoadAssetAtPath<FlavourRules>(DataRoot + "/FlavourRules.asset");
        }

        private static List<FlavourZone> LoadZones()
        {
            List<FlavourZone> zones = new List<FlavourZone>();

            foreach(string guid in AssetDatabase.FindAssets("t:FlavourZone", new[] { DataRoot + "/Zones" }))
            {
                zones.Add(AssetDatabase.LoadAssetAtPath<FlavourZone>(AssetDatabase.GUIDToAssetPath(guid)));
            }

            return zones;
        }

        private static IngredientDefinition Ingredient(string name)
        {
            IngredientDefinition ingredient = AssetDatabase.LoadAssetAtPath<IngredientDefinition>(DataRoot + "/Ingredients/" + name + ".asset");
            if(ingredient == null) Debug.LogWarning("[Cooking] Missing ingredient asset: " + name);
            return ingredient;
        }

        private static FlavourZone Zone(string name)
        {
            return AssetDatabase.LoadAssetAtPath<FlavourZone>(DataRoot + "/Zones/" + name + ".asset");
        }

        private static List<IngredientDefinition> LoadPantry()
        {
            List<IngredientDefinition> pantry = new List<IngredientDefinition>();

            foreach(string name in PantryOrder)
            {
                IngredientDefinition ingredient = Ingredient(name);
                if(ingredient != null) pantry.Add(ingredient);
            }

            return pantry;
        }

        // ------------------------------------------------------------------ Recipes (from the design doc)

        private struct Item
        {
            public string Name;
            public int Quantity;
            public PrepSteps Prep;

            public Item(string name, int quantity, PrepSteps prep = PrepSteps.None)
            {
                Name = name;
                Quantity = quantity;
                Prep = prep;
            }
        }

        private static Dictionary<string, RecipeDefinition> EnsureRecipes()
        {
            if(!AssetDatabase.IsValidFolder(RecipeFolder)) AssetDatabase.CreateFolder(DataRoot, "Recipes");

            const PrepSteps Peeled = PrepSteps.Peeled;
            const PrepSteps Sliced = PrepSteps.Sliced;
            const PrepSteps Grated = PrepSteps.Grated;
            const string Street = "Erlin's Eye street food";

            Dictionary<string, RecipeDefinition> recipes = new Dictionary<string, RecipeDefinition>();

            Add(recipes, "MSBeef fried rice", Street, CookMethods.Fried,
                new Item("Rice", 1), new Item("Real Beef", 1, Sliced), new Item("Onion", 1, Peeled | Sliced),
                new Item("MSG2 Cube", 3, Grated), new Item("Citric Acid", 1), new Item("Sugar", 2));
            Add(recipes, "Calorice", Street, CookMethods.Fried,
                new Item("Rice", 2), new Item("Tofu", 3, Sliced), new Item("Mushroom", 1, Sliced), new Item("Pepper", 1), new Item("Salt", 1));
            Add(recipes, "Hellchick noodles", Street, CookMethods.Fried,
                new Item("Seaweed Noodles", 1), new Item("Synthetic Chicken", 1, Sliced), new Item("Onion", 2, Peeled | Sliced),
                new Item("Chilli", 3, Grated), new Item("MSG2 Cube", 2, Grated), new Item("Sugar", 2));
            Add(recipes, "Mushroom soup", Street, CookMethods.Boiled,
                new Item("Soup Cube", 1), new Item("Mushroom", 3, Sliced), new Item("Potato", 2, Peeled | Sliced), new Item("Salt", 2), new Item("Pepper", 1));
            Add(recipes, "Synthetic soup", Street, CookMethods.Boiled,
                new Item("Soup Cube", 1), new Item("Seaweed Noodles", 1), new Item("Synthetic Chicken", 1, Sliced),
                new Item("MSG2 Cube", 1, Grated), new Item("Citric Acid", 2), new Item("Pepper", 1));
            Add(recipes, "Soup on the rock", Street, CookMethods.Boiled,
                new Item("Soup Cube", 1), new Item("Potato", 1, Peeled), new Item("Salt", 2));
            Add(recipes, "Boiled synthetic chicken", "Ordered by a confused sleeper", CookMethods.Boiled,
                new Item("Synthetic Chicken", 1));

            AssetDatabase.SaveAssets();
            return recipes;
        }

        private static void Add(Dictionary<string, RecipeDefinition> recipes, string name, string origin, CookMethods method, params Item[] items)
        {
            string path = RecipeFolder + "/" + name + ".asset";
            RecipeDefinition recipe = AssetDatabase.LoadAssetAtPath<RecipeDefinition>(path);

            if(recipe == null)
            {
                List<IngredientPortion> portions = new List<IngredientPortion>();

                foreach(Item item in items)
                {
                    // Everything goes into the same station, seasonings included.
                    portions.Add(new IngredientPortion(Ingredient(item.Name), item.Quantity, item.Prep, method));
                }

                recipe = ScriptableObject.CreateInstance<RecipeDefinition>();
                recipe.Configure(name, origin, portions);
                AssetDatabase.CreateAsset(recipe, path);
            }

            recipes[name] = recipe;
        }

        // ------------------------------------------------------------------ Customers (from the design doc's NPC notes)

        private static List<Customer> CreateCustomers(Dictionary<string, RecipeDefinition> recipes)
        {
            return new List<Customer>
            {
                new Customer
                {
                    name = "The Woman",
                    orders = new List<RecipeDefinition> { recipes["Calorice"], recipes["Synthetic soup"] },
                    secretDesire = Zone("Sweet and Sour"),
                    refuses = new List<IngredientDefinition> { Ingredient("Beef"), Ingredient("Real Beef") },
                    orderLines = new List<string> { "{0}. And hurry, the flux won't wait for anyone.", "{0}. Fried, greasy, salty. You know.", "{0}. No beef. Don't even look at the beef." },
                    likedLine = "Fine. That's fine.",
                    dislikedLine = "Is this what passes for food now?",
                    refusedLine = "I said no beef.",
                    dreamLine = "...I used to make this. For someone.",
                },
                new Customer
                {
                    name = "The Man",
                    orders = new List<RecipeDefinition> { recipes["Calorice"], recipes["Soup on the rock"] },
                    secretDesire = Zone("Hearty Broth"),
                    refuses = new List<IngredientDefinition> { Ingredient("Synthetic Chicken") },
                    orderLines = new List<string> { "{0}. It's the least bad option.", "{0}, please. Nothing synthetic.", "The {0}. The kids need me back soon." },
                    likedLine = "That's... decent. Thank you.",
                    dislikedLine = "Healthy, I suppose. Joyless.",
                    refusedLine = "Don't even get me started on the synthetic chicken.",
                    dreamLine = "Simple. Tasty. Nobody got hurt making it. Could I make this for the kids?",
                },
                new Customer
                {
                    name = "The Sleeper",
                    orders = new List<RecipeDefinition> { recipes["Boiled synthetic chicken"], recipes["Soup on the rock"] },
                    secretDesire = Zone("Fire Bowl"),
                    orderLines = new List<string> { "Uum... I'd like a {0}?", "{0}. Is that a thing people eat?" },
                    likedLine = "Wow! That kinda tastes. So that's why people stuff their face?",
                    dislikedLine = "Uhh... thanks?",
                    refusedLine = "Uhh... thanks?",
                    dreamLine = "That burn... I remember a kitchen. Someone's hands. I have to go.",
                },
                new Customer
                {
                    name = "Eyesider",
                    orders = new List<RecipeDefinition>(),
                    orderLines = new List<string> { "{0}.", "{0}. Quick.", "Just the {0}." },
                    likedLine = "...",
                    dislikedLine = "Tch.",
                    refusedLine = "Tch.",
                    dreamLine = "...huh.",
                },
            };
        }
    }
}
