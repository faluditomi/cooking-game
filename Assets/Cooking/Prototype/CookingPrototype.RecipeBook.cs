using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The tablet's Menu and History pages, sized for the tablet in the workbench. On the left a list
    /// (the menu's dishes, or every order served); on the right one scrolling page: the dish's recipe
    /// written like the Current Order card (or the order's report), with its flavour profile at the
    /// bottom. The kitchen keeps running while it's open.
    /// </summary>
    public partial class CookingPrototype
    {
        private GameObject recipeBookOverlay;
        private RectTransform bookList;
        private Text bookTitle;
        private Text bookSteps;
        private FlavourRadarGraphic bookRadar;
        private RecipeDefinition bookRecipe;
        private Text bookRadarHeader;
        private RectTransform bookPicture;
        private Button bookDeleteButton;
        private ScrollRect bookScroll;

        /// <summary>
        /// The menu, then your own dishes that aren't on it: regulars' favourites and dream meals you
        /// haven't named, or took off the menu. Recipes only NPCs know (like the Sleeper's first
        /// "meals") are never in the book.
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
            if(bookScroll != null) bookScroll.verticalNormalizedPosition = 1f;
            RefreshAll();
        }

        private int Owned(IngredientDefinition ingredient)
        {
            return inventory.Total(ingredient) + cart.Count(ingredient) + OnDesk(ingredient);
        }

        private void RefreshRecipeBook()
        {
            if(!RecipeBookOpen) return;

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
                bookRadar.ClearProfiles();
                return;
            }

            RecipeDefinition recipe = bookRecipe;
            BuildDishPicture(bookPicture, recipe.Portions);
            bookTitle.text = "<b>" + recipe.DisplayName + "</b>\n<size=12><color=#a6a199>" + DishType(recipe) + "</color></size>";
            // The recipe exactly as the Current Order card writes it.
            bookSteps.text = RecipeLines(recipe);

            // Only the selected dish: no zone bands, so its shape reads at a glance.
            bookRadar.ClearProfiles();
            bookRadar.SetZones(null);
            bookRadar.AddProfile(recipe.CalculateProfile(rules), new Color(0.95f, 0.55f, 0.2f, 1f), true);

            bookDeleteButton.transform.parent.gameObject.SetActive(CanDelete(recipe));
        }

        private void RebuildBookList()
        {
            UIFactory.ClearChildren(bookList);

            // Only the menu. Dishes you didn't save (or took off) live in the cooking history.
            foreach(RecipeDefinition recipe in VisibleMenu())
            {
                RecipeDefinition captured = recipe;
                Button button = AddListButton(recipe.DisplayName, recipe == bookRecipe, () => SelectRecipe(captured), 40f);
                PinLeft(button, DishIcon(button.transform, recipe.Portions, 32f), 32f);
            }
        }

        private Button AddListButton(string label, bool selected, UnityEngine.Events.UnityAction onClick, float height)
        {
            Button button = UIFactory.Button(bookList, label, onClick, selected ? Orange : UIFactory.ButtonColor, 13);
            Text text = button.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(8f, 0f);
            UIFactory.FitText(text);
            UIFactory.Size(button, height, 0f);
            return button;
        }

        /// <summary>The Menu and History pages of the tablet.</summary>
        private void BuildRecipeBook(Transform pages)
        {
            RectTransform page = UIFactory.Rect(pages, "Recipe Book");
            UIFactory.Stretch(page);
            recipeBookOverlay = page.gameObject;

            // Left: the list, with no background of its own.
            RectTransform left = UIFactory.Rect(page, "List");
            UIFactory.Place(left, 0f, 0f, 0.36f, 1f, 4f);
            VerticalLayoutGroup leftLayout = UIFactory.Vertical(left.gameObject, 4f, 2);
            leftLayout.childForceExpandHeight = false;
            bookList = UIFactory.ScrollList(left, 4f);

            // Right: one page that scrolls, the flavour profile at the bottom.
            Image sheet = UIFactory.Panel(page, "Page", UIFactory.PanelColor);
            UIFactory.Place(sheet.rectTransform, 0.36f, 0f, 1f, 1f, 4f);
            VerticalLayoutGroup sheetLayout = UIFactory.Vertical(sheet.gameObject, 0f, 8);
            sheetLayout.childForceExpandHeight = false;
            RectTransform detail = UIFactory.ScrollList(sheet.transform, 6f);
            bookScroll = detail.GetComponentInParent<ScrollRect>();

            RectTransform titleRow = UIFactory.Row(detail, 58f, 8f);
            titleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            bookPicture = UIFactory.Rect(titleRow, "Dish Picture");
            UIFactory.Size(bookPicture, 56f, 0f, 56f, 0f);
            bookTitle = UIFactory.Label(titleRow, "", 17, TextAnchor.MiddleLeft, FontStyle.Normal, UIFactory.TextColor);
            UIFactory.Size(bookTitle, -1f, -1f, -1f, 1f);
            UIFactory.FitText(bookTitle);

            bookSteps = UIFactory.Label(detail, "", 13);

            bookRadarHeader = UIFactory.Label(detail, "FLAVOUR PROFILE", 12, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.MutedText);
            UIFactory.Size(bookRadarHeader, 20f, 0f);
            RectTransform radarArea = UIFactory.Rect(detail, "Radar");
            UIFactory.Size(radarArea, 220f, 0f);
            bookRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            bookRadar.raycastTarget = false;
            bookRadar.SetLabelMargin(30f);
            bookRadar.CreateLabels(UIFactory.Font, 11, UIFactory.MutedText);

            // History page: save a dish that isn't on the menu.
            BuildHistorySaveRow(detail);

            RectTransform deleteRow = UIFactory.Row(detail, 30f);
            bookDeleteButton = UIFactory.Button(deleteRow, "Remove from the menu", DeleteFromMenu, new Color(0.35f, 0.2f, 0.2f, 1f), 13);

            recipeBookOverlay.SetActive(false);
        }
    }
}
