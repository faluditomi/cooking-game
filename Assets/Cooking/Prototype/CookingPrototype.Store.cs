using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The Shop tab on the tablet (mornings only). Pick ingredients into a cart, one by one from the shelf or by
    /// dish from the recipe book, then pay for the whole cart at checkout. Nothing is bought until then.
    /// </summary>
    public partial class CookingPrototype
    {
        private class ShelfCard
        {
            public IngredientDefinition Ingredient;
            public Text Info;
            public Button Less;
            public Button More;
            public Button MoreFive;
        }

        private readonly ShoppingCart cart = new ShoppingCart();
        private readonly List<ShelfCard> shelfCards = new List<ShelfCard>();
        private RectTransform cartContent;
        private RectTransform bundleGrid;
        /// <summary>The menu names the bundle tiles were built for, to rebuild them when it changes.</summary>
        private string bundleKey;
        private Text cartTotalText;
        private Button checkoutButton;
        private Button clearCartButton;
        private Button dayTimerButton;
        private Text regularsText;

        // ------------------------------------------------------------------ Actions

        private bool StoreOpen()
        {
            if(phase == Phase.Morning) return true;
            Say("The store is closed until tomorrow morning.", UIFactory.Bad);
            return false;
        }

        private void AddToCart(IngredientDefinition ingredient, int quantity)
        {
            if(!StoreOpen()) return;
            cart.Add(ingredient, quantity);
            Say(quantity + "× " + ingredient.DisplayName + " in the cart.");
            RefreshAll();
        }

        private void RemoveFromCart(IngredientDefinition ingredient)
        {
            cart.Remove(ingredient);
            RefreshAll();
        }

        private void AddServingToCart(RecipeDefinition recipe)
        {
            if(!StoreOpen()) return;
            int added = cart.AddRecipe(recipe);
            Say("Ingredients for one " + recipe.DisplayName + " (" + added + " units) in the cart.");
            RefreshAll();
        }

        private void AddMissingToCart(RecipeDefinition recipe)
        {
            if(!StoreOpen()) return;
            int added = cart.AddMissing(recipe, inventory);
            Say(added > 0
                ? "Added what " + recipe.DisplayName + " was missing (" + added + " units)."
                : "Your stock and cart already cover a " + recipe.DisplayName + ". Use \"+ One serving\" for another.");
            RefreshAll();
        }

        private void ClearCart()
        {
            cart.Clear();
            Say("Cart emptied.");
            RefreshAll();
        }

        private void Checkout()
        {
            if(!StoreOpen() || cart.IsEmpty) return;

            float total = cart.TotalCost;
            int units = cart.TotalUnits;

            if(!cart.Checkout(inventory, ref money))
            {
                Say("You can't afford all that: $" + total.ToString("0.00") + " in the cart, $" + money.ToString("0.00") + " in your pocket.", UIFactory.Bad);
                return;
            }

            spentToday += total;
            Say("Bought " + units + " units for $" + total.ToString("0.00") + ". They're in your stock.", UIFactory.Good);
            RefreshAll();
        }

        // ------------------------------------------------------------------ UI

        private GameObject BuildStoreArea(RectTransform root)
        {
            BuildShelf(Column(root, "Shelf", 0f, 0.66f));
            BuildCart(Column(root, "Cart", 0.66f, 1f));
            return root.gameObject;
        }

        private void BuildShelf(RectTransform shelf)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(shelf.gameObject, 12f, 16);
            layout.childForceExpandHeight = false;
            Header(shelf, "SHELF  <color=#77736d>pick ingredients into the cart, or shop by dish on the Recipes tab</color>");

            RectTransform grid = UIFactory.Rect(shelf, "Grid");
            GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(196f, 92f);
            gridLayout.spacing = new Vector2(8f, 8f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 4;
            UIFactory.Size(grid, 5 * 100f, 0f);

            foreach(IngredientDefinition ingredient in pantry)
            {
                if(ingredient == null) continue;

                IngredientDefinition captured = ingredient;
                ShelfCard card = new ShelfCard { Ingredient = ingredient };
                RectTransform panel = UIFactory.Panel(grid, ingredient.DisplayName, UIFactory.PanelDark).rectTransform;

                // The ingredient's picture on the left, name and stock beside it.
                Image icon = UIFactory.Icon(panel, ingredient.Icon, 0f);
                UIFactory.Place(icon.rectTransform, 0f, 0.4f, 0f, 1f, 4f).sizeDelta = new Vector2(52f, -8f);
                icon.rectTransform.anchoredPosition = new Vector2(30f, 0f);

                // The price sits on the line below (with stock), so long names don't wrap into it.
                Text name = UIFactory.Label(panel, ingredient.DisplayName, 18, TextAnchor.UpperLeft, FontStyle.Bold);
                UIFactory.FitText(name);
                UIFactory.Place(name.rectTransform, 0f, 0.55f, 1f, 1f).offsetMin = new Vector2(62f, 0f);
                name.rectTransform.offsetMax = new Vector2(-6f, -6f);

                card.Info = UIFactory.Label(panel, "", 14, TextAnchor.MiddleLeft, FontStyle.Normal, UIFactory.MutedText);
                UIFactory.FitText(card.Info);
                UIFactory.Place(card.Info.rectTransform, 0f, 0.38f, 1f, 0.62f).offsetMin = new Vector2(62f, 0f);

                RectTransform buttons = UIFactory.Rect(panel, "Buttons");
                UIFactory.Place(buttons, 0f, 0f, 1f, 0.4f, 6f);
                UIFactory.Horizontal(buttons.gameObject, 6f);
                card.Less = UIFactory.Button(buttons, "-", () => RemoveFromCart(captured), null, 18);
                card.More = UIFactory.Button(buttons, "+1", () => AddToCart(captured, 1), null, 17);
                card.MoreFive = UIFactory.Button(buttons, "+5", () => AddToCart(captured, 5), null, 17);

                shelfCards.Add(card);
            }

            // Bundles: everything one menu dish needs, in one tap.
            Header(shelf, "BUNDLES  <color=#77736d>all the ingredients for one serving</color>");
            bundleGrid = UIFactory.Rect(shelf, "Bundles");
            GridLayoutGroup bundleLayout = bundleGrid.gameObject.AddComponent<GridLayoutGroup>();
            bundleLayout.cellSize = new Vector2(196f, 64f);
            bundleLayout.spacing = new Vector2(8f, 8f);
            bundleLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            bundleLayout.constraintCount = 4;
            UIFactory.Size(bundleGrid, -1f, 1f);
        }

        private void RebuildBundles()
        {
            List<RecipeDefinition> visible = VisibleMenu();
            StringBuilder key = new StringBuilder();
            foreach(RecipeDefinition recipe in visible) key.Append(recipe.GetHashCode()).Append(recipe.DisplayName).Append('|');
            if(key.ToString() == bundleKey) return;

            bundleKey = key.ToString();
            UIFactory.ClearChildren(bundleGrid);

            foreach(RecipeDefinition recipe in visible)
            {
                RecipeDefinition captured = recipe;
                string label = recipe.DisplayName + "\n<size=14><color=#a6a199>$" + recipe.CalculateIngredientCost().ToString("0.00") + " · " + DishType(recipe) + "</color></size>";
                Button button = UIFactory.Button(bundleGrid, label, () => AddServingToCart(captured), new Color(0.24f, 0.27f, 0.36f, 1f), 17);
                button.interactable = phase == Phase.Morning;
            }
        }

        private void BuildCart(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;
            Header(column, "CART  <color=#77736d>nothing is bought until you check out</color>");
            cartContent = UIFactory.ScrollList(column, 4f);

            cartTotalText = UIFactory.Label(column, "", 19);
            UIFactory.Size(cartTotalText, 54f, 0f);

            RectTransform buyRow = UIFactory.Row(column, 54f);
            checkoutButton = UIFactory.Button(buyRow, "Buy everything", Checkout, null, 20);
            RectTransform clearRow = UIFactory.Row(column, 36f);
            clearCartButton = UIFactory.Button(clearRow, "Clear cart", ClearCart, new Color(0.35f, 0.2f, 0.2f, 1f), 17);

            // Pays for the cart (if anything's in it) and opens the stall.
            RectTransform startRow = UIFactory.Row(column, 58f);
            phaseButton = UIFactory.Button(startRow, "Buy and start the day", OpenStall, new Color(0.75f, 0.42f, 0.15f, 1f), 22);

            // For planning the day: the timer setting and who's coming.
            RectTransform timerRow = UIFactory.Row(column, 36f);
            dayTimerButton = UIFactory.Button(timerRow, "", ToggleDayTimer, null, 16);
            regularsText = UIFactory.Label(column, "", 15);
        }

        private void RefreshStore()
        {
            if(cartTotalText == null) return;

            bool open = phase == Phase.Morning;

            foreach(ShelfCard card in shelfCards)
            {
                int stock = inventory.Total(card.Ingredient);
                int inCart = cart.Count(card.Ingredient);
                card.Info.text = "$" + card.Ingredient.Price.ToString("0.00") + "   Stock " + stock + (inCart > 0 ? "  <color=#f28c33>Cart " + inCart + "</color>" : "");
                card.Less.interactable = open && inCart > 0;
                card.More.interactable = open;
                card.MoreFive.interactable = open;
            }

            if(shopPage != null && shopPage.activeSelf)
            {
                RebuildCartList();
                RebuildBundles();
                foreach(Button bundle in bundleGrid.GetComponentsInChildren<Button>()) bundle.interactable = open;
            }

            float total = cart.TotalCost;
            bool affordable = total <= money + 0.0001f;
            cartTotalText.text = cart.IsEmpty
                ? "<color=#a6a199>The cart is empty.</color>"
                : "Total <b>$" + total.ToString("0.00") + "</b>  <color=#a6a199>(" + cart.TotalUnits + " units)</color>\n"
                  + (affordable
                      ? "<color=#a6a199>You'll have $" + (money - total).ToString("0.00") + " left.</color>"
                      : "<color=#e5594d>$" + (total - money).ToString("0.00") + " more than you have.</color>");
            checkoutButton.interactable = open && !cart.IsEmpty && affordable;
            dayTimerButton.interactable = open;
            UIFactory.SetButtonText(dayTimerButton, dayTimer ? "Day timer: on (" + FormatTime(daySeconds) + ")" : "Day timer: off");
            regularsText.text = DescribeRegulars().TrimStart('\n') + "<color=#a6a199>Rent at the end of the cycle: $" + stallRent.ToString("0.00")
                                + ". Prepped stock spoils overnight; raw keeps.</color>";
            clearCartButton.interactable = !cart.IsEmpty;
        }

        private void RebuildCartList()
        {
            UIFactory.ClearChildren(cartContent);

            foreach(ShoppingCart.Line line in cart.Lines())
            {
                IngredientDefinition ingredient = line.Ingredient;
                RectTransform row = UIFactory.Row(cartContent, 34f, 4f);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UIFactory.Icon(row, ingredient.Icon, 32f);

                Text label = UIFactory.Label(row, line.Quantity + "× " + ingredient.DisplayName + "  <color=#a6a199>$" + line.Cost.ToString("0.00") + "</color>",
                    17, TextAnchor.MiddleLeft);
                UIFactory.Size(label, 34f, 0f, -1f, 1f);

                Button less = UIFactory.Button(row, "-", () => RemoveFromCart(ingredient), null, 18);
                UIFactory.Size(less, 34f, 0f, 36f, 0f);
                Button more = UIFactory.Button(row, "+", () => AddToCart(ingredient, 1), null, 18);
                UIFactory.Size(more, 34f, 0f, 36f, 0f);
            }
        }
    }
}
