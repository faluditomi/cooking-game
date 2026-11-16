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
        private Button recipeBookButton;
        private RecipeDefinition bookRecipe;
        private Text bookRadarHeader;
        private GameObject bookShopGroup;
        private Text bookHistoryInfo;
        private Button recipesPageButton;
        private Button historyPageButton;
        private Button bookDeleteButton;

        private bool RecipeBookOpen => recipeBookOverlay != null && recipeBookOverlay.activeSelf;

        /// <summary>
        /// The menu, then dishes regulars order that aren't on it: their favourites and dream meals
        /// you haven't named, or took off the menu.
        /// </summary>
        private List<RecipeDefinition> KnownRecipes()
        {
            List<RecipeDefinition> known = VisibleMenu();

            foreach(CustomerRelationship relationship in relationships.Values)
            {
                foreach(RecipeDefinition recipe in relationship.LikedMeals) if(recipe != null && !known.Contains(recipe)) known.Add(recipe);
                if(relationship.DreamMeal != null && !known.Contains(relationship.DreamMeal)) known.Add(relationship.DreamMeal);
            }

            return known;
        }

        private void ToggleRecipeBook()
        {
            if(RecipeBookOpen)
            {
                recipeBookOverlay.SetActive(false);
                RefreshAll();
                return;
            }

            // Open on the dish being cooked, if there is one.
            List<RecipeDefinition> known = KnownRecipes();
            if(orderIn && order != null && known.Contains(order)) bookRecipe = order;
            else if(bookRecipe == null || !known.Contains(bookRecipe)) bookRecipe = known.Count > 0 ? known[0] : null;

            recipeBookOverlay.SetActive(true);
            RefreshAll();
        }

        /// <summary>Opens the book on <paramref name="recipe"/> (an order coming in).</summary>
        private void OpenRecipeBookOn(RecipeDefinition recipe)
        {
            bookPage = BookPage.Recipes;
            if(recipe != null) bookRecipe = recipe;
            recipeBookOverlay.SetActive(true);
            RefreshAll();
        }

        /// <summary>Takes the selected dish off the menu. It stays known (regulars can still order it), just unnamed in their lines.</summary>
        private void DeleteFromMenu()
        {
            if(bookRecipe == null || !IsOnMenu(bookRecipe)) return;

            deletedDishes.Add(bookRecipe);
            Say("\"" + bookRecipe.DisplayName + "\" is off the menu.");
            List<RecipeDefinition> known = KnownRecipes();
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
            return inventory.Total(ingredient) + cart.Count(ingredient) + (boardUnit != null && boardUnit.ingredient == ingredient ? 1 : 0);
        }

        private void RefreshRecipeBook()
        {
            if(recipeBookButton == null) return;
            UIFactory.SetButtonText(recipeBookButton, RecipeBookOpen ? "Close recipe book" : "Recipe book");
            if(!RecipeBookOpen) return;

            Color active = new Color(0.75f, 0.42f, 0.15f, 1f);
            recipesPageButton.GetComponent<Image>().color = bookPage == BookPage.Recipes ? active : UIFactory.ButtonColor;
            historyPageButton.GetComponent<Image>().color = bookPage == BookPage.History ? active : UIFactory.ButtonColor;
            bookShopGroup.SetActive(bookPage == BookPage.Recipes);
            bookHistoryInfo.gameObject.SetActive(bookPage == BookPage.History);
            bookDeleteButton.transform.parent.gameObject.SetActive(false);

            if(bookPage == BookPage.History)
            {
                RefreshHistoryPage();
                return;
            }

            bookRadarHeader.text = "FLAVOUR";
            RebuildBookList();

            if(bookRecipe == null)
            {
                bookTitle.text = "No recipes yet.";
                bookSteps.text = "";
                bookNeeds.text = "";
                bookServingButton.interactable = false;
                bookMissingButton.interactable = false;
                bookDeleteButton.transform.parent.gameObject.SetActive(false);
                return;
            }

            RecipeDefinition recipe = bookRecipe;
            FlavourProfile profile = recipe.CalculateProfile(rules);
            FlavourZone zone = FlavourEvaluator.FindBestZone(profile, zones, Falloff).Zone;

            StringBuilder title = new StringBuilder();
            title.Append("<b>").Append(recipe.DisplayName).Append("</b>\n<size=18><color=#a6a199>");
            title.Append(DishType(recipe)).Append("   ");
            if(!IsOnMenu(recipe)) title.Append("<color=#f28c33>Not on the menu</color>   ");
            if(!string.IsNullOrEmpty(recipe.Origin)) title.Append("Origin: ").Append(recipe.Origin).Append("   ");
            title.Append("Menu price $").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00"));
            title.Append("   Ingredients $").Append(recipe.CalculateIngredientCost().ToString("0.00")).Append("</color>");
            if(zone != null) title.Append("\nTastes like <color=#").Append(ColorUtility.ToHtmlStringRGB(zone.Color)).Append(">").Append(zone.DisplayName).Append("</color>");
            else title.Append("\n<color=#a6a199>Not in any desirable zone.</color>");
            title.Append("</size>");
            bookTitle.text = title.ToString();

            bookSteps.text = RecipeGuide.Steps(recipe, Owned);

            bookRadar.SetHighlight(zone);
            bookRadar.ClearProfiles();
            // Light rather than orange, so the dish stands out from zones like Greasy Comfort.
            bookRadar.AddProfile(profile, new Color(0.95f, 0.93f, 0.86f, 1f), true);

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

            bookDeleteButton.transform.parent.gameObject.SetActive(IsOnMenu(recipe));

            bool shopOpen = phase == Phase.Morning;
            bookServingButton.interactable = shopOpen;
            bookMissingButton.interactable = shopOpen;
            bookShopHint.text = shopOpen
                ? "<color=#a6a199>Adds to the store cart. Pay at checkout in the Store.</color>"
                : "<color=#a6a199>The store opens in the morning.</color>";
        }

        private void RebuildBookList()
        {
            UIFactory.ClearChildren(bookList);
            List<RecipeDefinition> known = KnownRecipes();
            bool learnedHeader = false;

            Text menuHeader = Header(bookList, "MENU");
            UIFactory.Size(menuHeader, 24f, 0f);

            foreach(RecipeDefinition recipe in known)
            {
                if(!learnedHeader && !IsOnMenu(recipe))
                {
                    learnedHeader = true;
                    Text header = Header(bookList, "NOT ON THE MENU");
                    UIFactory.Size(header, 30f, 0f);
                }

                RecipeDefinition captured = recipe;
                bool selected = recipe == bookRecipe;
                bool ordered = orderIn && recipe == order;
                string label = recipe.DisplayName + (ordered ? "  <color=#f28c33>ordered</color>" : "");
                Button button = UIFactory.Button(bookList, label, () => SelectRecipe(captured),
                    selected ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor, 18);
                Text text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                UIFactory.Size(button, 42f, 0f);
            }
        }

        private void BuildRecipeBook(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Recipe Book", new Color(0f, 0f, 0f, 0.7f));
            UIFactory.Stretch(overlay.rectTransform);
            overlay.rectTransform.anchorMax = new Vector2(1f - SidebarWidth, 1f);
            // Leave the top bar clickable (the same button closes the book) and the message bar visible.
            overlay.rectTransform.offsetMax = new Vector2(0f, -60f);
            overlay.rectTransform.offsetMin = new Vector2(0f, 44f);
            recipeBookOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Book", UIFactory.PanelColor).rectTransform;
            UIFactory.Stretch(panel, 24f);

            // Left: recipes you know.
            RectTransform left = UIFactory.Panel(panel, "Recipes", UIFactory.PanelDark).rectTransform;
            UIFactory.Place(left, 0f, 0f, 0.24f, 1f, 12f);
            VerticalLayoutGroup leftLayout = UIFactory.Vertical(left.gameObject, 8f, 14);
            leftLayout.childForceExpandHeight = false;
            Text bookHeader = UIFactory.Label(left, "RECIPE BOOK", 22, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(bookHeader, 32f, 0f);
            RectTransform pages = UIFactory.Row(left, 40f, 6f);
            recipesPageButton = UIFactory.Button(pages, "Recipes", () => ShowBookPage(BookPage.Recipes), null, 18);
            historyPageButton = UIFactory.Button(pages, "History", () => ShowBookPage(BookPage.History), null, 18);
            bookList = UIFactory.ScrollList(left, 6f);

            // Middle: the steps.
            RectTransform middle = UIFactory.Rect(panel, "Steps");
            UIFactory.Place(middle, 0.24f, 0f, 0.66f, 1f, 18f);
            VerticalLayoutGroup middleLayout = UIFactory.Vertical(middle.gameObject, 12f, 0);
            middleLayout.childForceExpandHeight = false;
            bookTitle = UIFactory.Label(middle, "", 32, TextAnchor.UpperLeft, FontStyle.Normal, UIFactory.TextColor);
            RectTransform stepsScroll = UIFactory.ScrollList(middle, 0f);
            bookSteps = UIFactory.Label(stepsScroll, "", 19);

            // Right: flavour and shopping.
            RectTransform right = UIFactory.Panel(panel, "Flavour and Shopping", UIFactory.PanelDark).rectTransform;
            UIFactory.Place(right, 0.66f, 0f, 1f, 1f, 12f);
            VerticalLayoutGroup rightLayout = UIFactory.Vertical(right.gameObject, 10f, 14);
            rightLayout.childForceExpandHeight = false;
            bookRadarHeader = Header(right, "FLAVOUR");

            RectTransform radarArea = UIFactory.Rect(right, "Radar");
            UIFactory.Size(radarArea, 260f, 1f);
            bookRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            bookRadar.raycastTarget = false;
            bookRadar.SetZones(zones);
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

            RectTransform deleteRow = UIFactory.Row(right, 38f);
            bookDeleteButton = UIFactory.Button(deleteRow, "Remove from the menu", DeleteFromMenu, new Color(0.35f, 0.2f, 0.2f, 1f), 16);

            RectTransform closeRow = UIFactory.Row(right, 46f);
            UIFactory.Button(closeRow, "Close", ToggleRecipeBook, new Color(0.75f, 0.42f, 0.15f, 1f), 20);

            recipeBookOverlay.SetActive(false);
        }
    }
}
