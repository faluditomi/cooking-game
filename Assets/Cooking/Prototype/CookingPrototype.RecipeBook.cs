using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The recipe book: every dish you know, what it's made of, how to prep and cook it, and its
    /// flavour. Opens from the top bar in any area. In the morning it can also fill the cart.
    /// The kitchen keeps running while it's open, like glancing at a tablet on the workbench.
    /// </summary>
    public partial class CookingPrototype
    {
        private GameObject recipeBookOverlay;
        private RectTransform bookList;
        private Text bookTitle;
        private Text bookSteps;
        private Text bookNeeds;
        private FlavourRadarGraphic bookRadar;
        private Button bookServingButton;
        private Button bookMissingButton;
        private Text bookShopHint;
        private RecipeDefinition bookRecipe;
        private Text bookRadarHeader;
        private GameObject bookShopGroup;
        private Text bookHistoryInfo;
        private Button recipesPageButton;
        private RectTransform bookPicture;
        private Button historyPageButton;
        private Button bookDeleteButton;

        /// <summary>
        /// The menu, then your own dishes that aren't on it: regulars' favourites and dream meals you
        /// haven't named, or took off the menu. Recipes only NPCs know (like the Sleeper's boiled
        /// synthetic chicken) are never in the book.
        /// </summary>
        private List<RecipeDefinition> KnownRecipes()
        {
            List<RecipeDefinition> known = VisibleMenu();

            foreach(CustomerRelationship relationship in relationships.Values)
            {
                foreach(RecipeDefinition recipe in relationship.LikedMeals) if(IsPlayerDish(recipe) && !known.Contains(recipe)) known.Add(recipe);
                if(IsPlayerDish(relationship.DreamMeal) && !known.Contains(relationship.DreamMeal)) known.Add(relationship.DreamMeal);
            }

            return known;
        }

        /// <summary>A dish you made in this session (as opposed to a recipe asset).</summary>
        private static bool IsPlayerDish(RecipeDefinition recipe)
        {
            return recipe != null && (recipe.hideFlags & HideFlags.DontSave) != 0;
        }

        /// <summary>Only your own saved dishes can come off the menu; the default recipes stay.</summary>
        private bool CanDelete(RecipeDefinition recipe)
        {
            return IsOnMenu(recipe) && IsPlayerDish(recipe);
        }

        /// <summary>Takes the selected dish off the menu. It stays known (regulars can still order it), just unnamed in their lines.</summary>
        private void DeleteFromMenu()
        {
            if(bookRecipe == null || !CanDelete(bookRecipe)) return;

            deletedDishes.Add(bookRecipe);
            Say("\"" + bookRecipe.DisplayName + "\" is off the menu.");
            List<RecipeDefinition> known = VisibleMenu();
            if(!known.Contains(bookRecipe)) bookRecipe = known.Count > 0 ? known[0] : null;
            RefreshAll();
        }

        private void SelectRecipe(RecipeDefinition recipe)
        {
            bookRecipe = recipe;
            RefreshAll();
        }

        private int Owned(IngredientDefinition ingredient)
        {
            return inventory.Total(ingredient) + cart.Count(ingredient) + OnDesk(ingredient);
        }

        private void RefreshRecipeBook()
        {
            if(!RecipeBookOpen) return;

            Color active = new Color(0.75f, 0.42f, 0.15f, 1f);
            recipesPageButton.GetComponent<Image>().color = bookPage == BookPage.Recipes ? active : UIFactory.ButtonColor;
            historyPageButton.GetComponent<Image>().color = bookPage == BookPage.History ? active : UIFactory.ButtonColor;
            // No shopping here any more: that's the Shop's job.
            bookShopGroup.SetActive(false);
            bookHistoryInfo.gameObject.SetActive(false);
            bookDeleteButton.transform.parent.gameObject.SetActive(false);
            if(bookPage != BookPage.History) historySaveRow.SetActive(false);

            if(bookPage == BookPage.History)
            {
                RefreshHistoryPage();
                return;
            }

            bookRadarHeader.text = "FLAVOUR PROFILE";
            RebuildBookList();

            if(bookRecipe == null)
            {
                bookTitle.text = "No recipes yet.";
                UIFactory.ClearChildren(bookPicture);
                bookSteps.text = "";
                bookNeeds.text = "";
                bookServingButton.interactable = false;
                bookMissingButton.interactable = false;
                bookDeleteButton.transform.parent.gameObject.SetActive(false);
                return;
            }

            RecipeDefinition recipe = bookRecipe;
            FlavourProfile profile = recipe.CalculateProfile(rules);
            BuildDishPicture(bookPicture, recipe.Portions);

            StringBuilder title = new StringBuilder();
            title.Append("<b>").Append(recipe.DisplayName).Append("</b>\n<size=18><color=#a6a199>");
            title.Append(DishType(recipe)).Append("   ");
            if(!IsOnMenu(recipe)) title.Append("<color=#f28c33>Not on the menu</color>   ");
            title.Append("Menu price $").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00"));
            title.Append("   Ingredients $").Append(recipe.CalculateIngredientCost().ToString("0.00")).Append("</color>");
            title.Append("</size>");
            bookTitle.text = title.ToString();

            bookSteps.text = RecipeGuide.Steps(recipe, Owned);

            // Only the selected dish: no zone bands, so its shape reads at a glance.
            bookRadar.ClearProfiles();
            bookRadar.AddProfile(profile, new Color(0.95f, 0.55f, 0.2f, 1f), true);

            // Shopping: have/need per ingredient, counting stock, cart and the prep board.
            StringBuilder needs = new StringBuilder();
            bool covered = true;

            foreach(KeyValuePair<IngredientDefinition, int> need in ShoppingCart.Needs(recipe))
            {
                int owned = Owned(need.Key);
                if(owned < need.Value) covered = false;
                if(needs.Length > 0) needs.Append(",  ");
                needs.Append("<color=#").Append(owned >= need.Value ? "73cc73" : "a6a199").Append(">")
                     .Append(need.Key.DisplayName).Append(' ').Append(owned).Append('/').Append(need.Value).Append("</color>");
            }

            bookNeeds.text = (covered ? "<color=#73cc73>You have enough for one.</color>\n" : "") + needs;

            bookDeleteButton.transform.parent.gameObject.SetActive(CanDelete(recipe));

            bool shopOpen = phase == Phase.Morning;
            bookServingButton.interactable = shopOpen;
            bookMissingButton.interactable = shopOpen;
            bookShopHint.text = shopOpen
                ? "<color=#a6a199>Adds to the cart on the Shop tab.</color>"
                : "<color=#a6a199>The store opens in the morning.</color>";
        }

        private void RebuildBookList()
        {
            UIFactory.ClearChildren(bookList);

            // Only the menu. Dishes you didn't save (or took off) live in the cooking history.

            foreach(RecipeDefinition recipe in VisibleMenu())
            {
                RecipeDefinition captured = recipe;
                bool selected = recipe == bookRecipe;
                string label = recipe.DisplayName;
                Button button = UIFactory.Button(bookList, label, () => SelectRecipe(captured),
                    selected ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor, 18);
                Text text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                UIFactory.Size(button, 52f, 0f);
                PinLeft(button, DishIcon(button.transform, recipe.Portions, 44f), 44f);
            }
        }

        /// <summary>The Recipes and History pages of the tablet.</summary>
        private void BuildRecipeBook(Transform pages)
        {
            RectTransform page = UIFactory.Rect(pages, "Recipe Book");
            UIFactory.Stretch(page);
            recipeBookOverlay = page.gameObject;

            RectTransform panel = UIFactory.Rect(page, "Book");
            UIFactory.Stretch(panel, 8f);

            // The selected dish and its flavour profile share one page, indented from the list.
            Image sheet = UIFactory.Panel(panel, "Page", UIFactory.PanelColor);
            UIFactory.Place(sheet.rectTransform, 0.24f, 0f, 1f, 1f, 6f);

            // Left: the menu, with no background of its own.
            RectTransform left = UIFactory.Rect(panel, "Recipes");
            UIFactory.Place(left, 0f, 0f, 0.24f, 1f, 12f);
            VerticalLayoutGroup leftLayout = UIFactory.Vertical(left.gameObject, 8f, 14);
            leftLayout.childForceExpandHeight = false;
            // The tablet's tabs switch pages; these stay for the page colours but are hidden.
            RectTransform pageRow = UIFactory.Row(left, 40f, 6f);
            recipesPageButton = UIFactory.Button(pageRow, "Recipes", () => ShowBookPage(BookPage.Recipes), null, 18);
            historyPageButton = UIFactory.Button(pageRow, "History", () => ShowBookPage(BookPage.History), null, 18);
            pageRow.gameObject.SetActive(false);
            bookList = UIFactory.ScrollList(left, 6f);

            // Middle: the steps.
            RectTransform middle = UIFactory.Rect(panel, "Steps");
            UIFactory.Place(middle, 0.24f, 0f, 0.66f, 1f, 18f);
            VerticalLayoutGroup middleLayout = UIFactory.Vertical(middle.gameObject, 12f, 0);
            middleLayout.childForceExpandHeight = false;
            RectTransform titleRow = UIFactory.Row(middle, 130f, 14f);
            titleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            bookPicture = UIFactory.Rect(titleRow, "Dish Picture");
            UIFactory.Size(bookPicture, 130f, 0f, 130f, 0f);
            bookTitle = UIFactory.Label(titleRow, "", 32, TextAnchor.MiddleLeft, FontStyle.Normal, UIFactory.TextColor);
            UIFactory.Size(bookTitle, -1f, -1f, -1f, 1f);
            UIFactory.FitText(bookTitle);
            RectTransform stepsScroll = UIFactory.ScrollList(middle, 0f);
            bookSteps = UIFactory.Label(stepsScroll, "", 19);

            // Right: the flavour profile, on the same page.
            RectTransform right = UIFactory.Rect(panel, "Flavour");
            UIFactory.Place(right, 0.66f, 0f, 1f, 1f, 18f);
            VerticalLayoutGroup rightLayout = UIFactory.Vertical(right.gameObject, 10f, 14);
            rightLayout.childForceExpandHeight = false;
            bookRadarHeader = Header(right, "FLAVOUR PROFILE");

            RectTransform radarArea = UIFactory.Rect(right, "Radar");
            UIFactory.Size(radarArea, 260f, 1f);
            bookRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            bookRadar.raycastTarget = false;
            // No zones: the book's radar shows just the dish (the title says which zone it tastes like).
            bookRadar.CreateLabels(UIFactory.Font, 15, UIFactory.MutedText);

            // Recipes page: shopping for the selected dish.
            RectTransform shop = UIFactory.Rect(right, "Shopping");
            VerticalLayoutGroup shopLayout = UIFactory.Vertical(shop.gameObject, 10f, 0);
            shopLayout.childForceExpandHeight = false;
            bookShopGroup = shop.gameObject;
            Header(shop, "SHOPPING  <color=#77736d>stock + cart</color>");
            bookNeeds = UIFactory.Label(shop, "", 17);
            RectTransform shopRow = UIFactory.Row(shop, 44f, 8f);
            bookServingButton = UIFactory.Button(shopRow, "+ One serving", () => { if(bookRecipe != null) AddServingToCart(bookRecipe); }, null, 18);
            bookMissingButton = UIFactory.Button(shopRow, "+ What's missing", () => { if(bookRecipe != null) AddMissingToCart(bookRecipe); },
                new Color(0.25f, 0.4f, 0.25f, 1f), 18);
            bookShopHint = UIFactory.Label(shop, "", 15);
            UIFactory.Size(bookShopHint, 22f, 0f);

            // History page: what you know about the selected regular.
            bookHistoryInfo = UIFactory.Label(right, "", 17);
            bookHistoryInfo.gameObject.SetActive(false);
            BuildHistorySaveRow(right);

            RectTransform deleteRow = UIFactory.Row(right, 38f);
            bookDeleteButton = UIFactory.Button(deleteRow, "Remove from the menu", DeleteFromMenu, new Color(0.35f, 0.2f, 0.2f, 1f), 16);


            recipeBookOverlay.SetActive(false);
        }
    }
}
