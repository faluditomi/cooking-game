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

        /// <summary>Recipes regulars can order that the player doesn't get in the book (meeting decision).</summary>
        private static readonly string[] OffMenu = { "Boiled synthetic chicken", "Potato", "Peeled potato", "MSG2 cube" };

        private const string CharacterArt = "Assets/Art Assets/Characters";

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
            // Some recipes only NPCs know: they can order them, but they're not on the menu or in the book.
            List<RecipeDefinition> menu = new List<RecipeDefinition>(recipes.Values);
            menu.RemoveAll(recipe => System.Array.IndexOf(OffMenu, recipe.DisplayName) >= 0);
            prototype.Setup(LoadRules(), LoadZones(), LoadPantry(), menu, CreateCustomers(recipes));
            EditorUtility.SetDirty(prototype);

            EditorSceneManager.SaveScene(scene, ScenePath);

            // Dialogue graphs, catalog, director and the regulars' dialogue ids.
            CookingDialogueSetup.BuildDialogue();

            // The billboards and the rendered cookware.
            AssignMarketArt();
            CookwareSprites.AssignToScene();
            UiSkinSetup.Assign();

            Selection.activeGameObject = root;
            Debug.Log("[Cooking] Built " + ScenePath + ". Press Play to cook.");
        }

        private const string BillboardRoot = "Assets/Art Assets/Bilboards Decorations";

        /// <summary>
        /// Puts the billboard ads and frame on the prototype in the open scene, with point filtering so
        /// the pixel art stays sharp. Safe to run again; does nothing when the art isn't in the project.
        /// </summary>
        [MenuItem("Custom Tools/Cooking Prototype/Assign Market Art")]
        public static void AssignMarketArt()
        {
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();

            if(prototype == null)
            {
                Debug.LogWarning("[Cooking] Open the prototype scene first.");
                return;
            }

            List<Sprite> ads = new List<Sprite>();

            if(AssetDatabase.IsValidFolder(BillboardRoot + "/1 Ad/128x64"))
            {
                foreach(string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { BillboardRoot + "/1 Ad/128x64" }))
                {
                    Sprite ad = LoadPixelSprite(AssetDatabase.GUIDToAssetPath(guid));
                    if(ad != null) ads.Add(ad);
                }
            }

            Sprite frame = LoadPixelSprite(BillboardRoot + "/2 Billboard/128x64.png");
            prototype.SetMarketArt(ads, frame);
            EditorUtility.SetDirty(prototype);
            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
            Debug.Log("[Cooking] Market art: " + ads.Count + " ads" + (frame != null ? " and the billboard frame." : ", no frame found."));
        }

        private static Sprite LoadPixelSprite(string path)
        {
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer == null) return null;

            if(importer.textureType != TextureImporterType.Sprite || importer.filterMode != FilterMode.Point
               || importer.textureCompression != TextureImporterCompression.Uncompressed)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
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
                // Secret desire zones belong to the regulars, not the zones the game shows.
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if(path.Contains("/Desires/")) continue;
                zones.Add(AssetDatabase.LoadAssetAtPath<FlavourZone>(path));
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

            // The Sleeper's first "meals" (Miro board): things they don't know aren't food. Served raw.
            const string Sleeper = "Ordered by a sleeper who doesn't know food yet";
            Add(recipes, "Potato", Sleeper, CookMethods.None, new Item("Potato", 1));
            Add(recipes, "Peeled potato", Sleeper, CookMethods.None, new Item("Potato", 1, Peeled));
            Add(recipes, "MSG2 cube", Sleeper, CookMethods.None, new Item("MSG2 Cube", 1));

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

        /// <summary>A regular's picture from the Miro board, trimmed and imported as a sprite. Null if it isn't there.</summary>
        private static Sprite Portrait(string name)
        {
            string path = CharacterArt + "/" + name + ".png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer == null) return null;

            if(importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Single || importer.maxTextureSize != 1024)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static List<Customer> CreateCustomers(Dictionary<string, RecipeDefinition> recipes)
        {
            // Each regular's secret desire: a flavour no menu recipe reaches, to be discovered.
            Dictionary<string, FlavourZone> desires = CookingDesireSetup.EnsureDesireZones();

            return new List<Customer>
            {
                new Customer
                {
                    name = "The Woman",
                    // Miro board: she orders MSBeef fried rice (Real Beef™ only) and won't touch the beef
                    // animal, except in her lost love's sweet and sour soup, which needs it.
                    // Her initial liked meals, from the board.
                    orders = new List<RecipeDefinition> { recipes["MSBeef fried rice"], recipes["Synthetic soup"], recipes["Calorice"] },
                    secretDesire = desires["The Woman"],
                    dreamNeeds = new List<IngredientDefinition> { Ingredient("Beef") },
                    refuses = new List<IngredientDefinition> { Ingredient("Beef") },
                    hurryAfter = 45f,
                    orderLines = new List<string> { "{0}. Get on it!" },
                    likedLine = "That hits the spot.",
                    dislikedLine = "What even is this, Pot Boy?",
                    refusedLine = "None of that beef shit, you hear me?",
                    dreamLine = "This... This is...",
                    portrait = Portrait("the_woman"),
                },
                new Customer
                {
                    name = "The Man",
                    orders = new List<RecipeDefinition> { recipes["Calorice"], recipes["Soup on the rock"] },
                    secretDesire = desires["The Man"],
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
                    // Miro board: their initial liked meals aren't on the menu and aren't really food.
                    // The first thing that tastes good replaces them and makes them Familiar; as a
                    // Stranger they don't know food, so they always pay.
                    orders = new List<RecipeDefinition>
                    {
                        recipes["Potato"], recipes["Peeled potato"], recipes["MSG2 cube"], recipes["Boiled synthetic chicken"],
                    },
                    firstTasteReplacesLikes = true,
                    alwaysPaysAsStranger = true,
                    likedMealsToFamiliar = 1,
                    portrait = Portrait("the_sleeper"),
                    secretDesire = desires["The Sleeper"],
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
