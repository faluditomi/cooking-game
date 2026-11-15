using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The Store area (mornings only). Pick ingredients into a cart, one by one from the shelf or by
    /// dish from the menu, then pay for the whole cart at checkout. Nothing is bought until then.
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

        private class MenuCard
        {
            public RecipeDefinition Recipe;
            public Text Text;
            public Button AddServing;
            public Button AddMissing;
        }

        private readonly ShoppingCart cart = new ShoppingCart();
        private readonly List<ShelfCard> shelfCards = new List<ShelfCard>();
        private readonly List<MenuCard> menuCards = new List<MenuCard>();
        private Text regularsText;
        private RectTransform cartContent;
        private Text cartTotalText;
        private Button checkoutButton;
        private Button clearCartButton;

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
            BuildShelf(Column(root, "Shelf", 0f, 0.47f));
            BuildMenu(Column(root, "Menu", 0.47f, 0.75f));
            BuildCart(Column(root, "Cart", 0.75f, 1f));
            return root.gameObject;
        }

        private void BuildShelf(RectTransform shelf)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(shelf.gameObject, 12f, 16);
            layout.childForceExpandHeight = false;
            Header(shelf, "SHELF  <color=#77736d>pick ingredients into the cart</color>");

            RectTransform grid = UIFactory.Rect(shelf, "Grid");
            GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(278f, 96f);
            gridLayout.spacing = new Vector2(9f, 9f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 3;
            UIFactory.Size(grid, -1f, 1f);

            foreach(IngredientDefinition ingredient in pantry)
            {
                if(ingredient == null) continue;

                IngredientDefinition captured = ingredient;
                ShelfCard card = new ShelfCard { Ingredient = ingredient };
                RectTransform panel = UIFactory.Panel(grid, ingredient.DisplayName, UIFactory.PanelDark).rectTransform;

                Text name = UIFactory.Label(panel, ingredient.DisplayName + "  <size=15><color=#a6a199>$" + ingredient.Price.ToString("0.00") + "</color></size>",
                    19, TextAnchor.UpperLeft, FontStyle.Bold);
                UIFactory.Place(name.rectTransform, 0f, 0.55f, 1f, 1f).offsetMin = new Vector2(10f, 0f);
                name.rectTransform.offsetMax = new Vector2(-6f, -6f);

                card.Info = UIFactory.Label(panel, "", 14, TextAnchor.MiddleLeft, FontStyle.Normal, UIFactory.MutedText);
                UIFactory.Place(card.Info.rectTransform, 0f, 0.38f, 1f, 0.62f).offsetMin = new Vector2(10f, 0f);

                RectTransform buttons = UIFactory.Rect(panel, "Buttons");
                UIFactory.Place(buttons, 0f, 0f, 1f, 0.4f, 6f);
                UIFactory.Horizontal(buttons.gameObject, 6f);
                card.Less = UIFactory.Button(buttons, "−", () => RemoveFromCart(captured), null, 18);
                card.More = UIFactory.Button(buttons, "+1", () => AddToCart(captured, 1), null, 17);
                card.MoreFive = UIFactory.Button(buttons, "+5", () => AddToCart(captured, 5), null, 17);

                shelfCards.Add(card);
            }
        }

        private void BuildMenu(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;
            Header(column, "MENU  <color=#77736d>shop by the dishes you know</color>");
            RectTransform list = UIFactory.ScrollList(column, 10f);

            foreach(RecipeDefinition recipe in menu)
            {
                if(recipe == null) continue;

                RecipeDefinition captured = recipe;
                MenuCard card = new MenuCard { Recipe = recipe };
                RectTransform panel = UIFactory.Panel(list, recipe.DisplayName, UIFactory.PanelDark).rectTransform;
                UIFactory.Vertical(panel.gameObject, 6f, 10);
                card.Text = UIFactory.Label(panel, "", 16);

                RectTransform row = UIFactory.Row(panel, 36f, 6f);
                card.AddServing = UIFactory.Button(row, "+ One serving", () => AddServingToCart(captured), null, 16);
                card.AddMissing = UIFactory.Button(row, "+ What's missing", () => AddMissingToCart(captured), new Color(0.25f, 0.4f, 0.25f, 1f), 16);
                menuCards.Add(card);
            }

            regularsText = UIFactory.Label(list, "", 16);
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
            checkoutButton = UIFactory.Button(buyRow, "Buy everything", Checkout, new Color(0.75f, 0.42f, 0.15f, 1f), 22);
            RectTransform clearRow = UIFactory.Row(column, 36f);
            clearCartButton = UIFactory.Button(clearRow, "Clear cart", ClearCart, new Color(0.35f, 0.2f, 0.2f, 1f), 17);
        }

        private void RefreshStore()
        {
            if(cartTotalText == null) return;

            bool open = phase == Phase.Morning;

            foreach(ShelfCard card in shelfCards)
            {
                int stock = inventory.Total(card.Ingredient);
                int inCart = cart.Count(card.Ingredient);
                card.Info.text = "Stock " + stock + (inCart > 0 ? "   <color=#f28c33>In cart " + inCart + "</color>" : "");
                card.Less.interactable = open && inCart > 0;
                card.More.interactable = open;
                card.MoreFive.interactable = open;
            }

            foreach(MenuCard card in menuCards)
            {
                card.Text.text = DescribeRecipeNeeds(card.Recipe);
                card.AddServing.interactable = open;
                card.AddMissing.interactable = open;
            }

            regularsText.text = DescribeRegulars();

            if(area == Area.Store) RebuildCartList();

            float total = cart.TotalCost;
            bool affordable = total <= money + 0.0001f;
            cartTotalText.text = cart.IsEmpty
                ? "<color=#a6a199>The cart is empty.</color>"
                : "Total <b>$" + total.ToString("0.00") + "</b>  <color=#a6a199>(" + cart.TotalUnits + " units)</color>\n"
                  + (affordable
                      ? "<color=#a6a199>You'll have $" + (money - total).ToString("0.00") + " left.</color>"
                      : "<color=#e5594d>$" + (total - money).ToString("0.00") + " more than you have.</color>");
            checkoutButton.interactable = open && !cart.IsEmpty && affordable;
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

                Text label = UIFactory.Label(row, line.Quantity + "× " + ingredient.DisplayName + "  <color=#a6a199>$" + line.Cost.ToString("0.00") + "</color>",
                    17, TextAnchor.MiddleLeft);
                UIFactory.Size(label, 34f, 0f, -1f, 1f);

                Button less = UIFactory.Button(row, "−", () => RemoveFromCart(ingredient), null, 18);
                UIFactory.Size(less, 34f, 0f, 36f, 0f);
                Button more = UIFactory.Button(row, "+", () => AddToCart(ingredient, 1), null, 18);
                UIFactory.Size(more, 34f, 0f, 36f, 0f);
            }
        }

        /// <summary>One menu card: price, and have/need per ingredient (stock + cart).</summary>
        private string DescribeRecipeNeeds(RecipeDefinition recipe)
        {
            StringBuilder line = new StringBuilder();
            bool covered = true;

            foreach(KeyValuePair<IngredientDefinition, int> need in ShoppingCart.Needs(recipe))
            {
                int have = inventory.Total(need.Key) + cart.Count(need.Key);
                if(have < need.Value) covered = false;
                if(line.Length > 0) line.Append(",  ");
                line.Append("<color=#").Append(have >= need.Value ? "73cc73" : "a6a199").Append(">")
                    .Append(need.Key.DisplayName).Append(' ').Append(have).Append('/').Append(need.Value).Append("</color>");
            }

            return "<b>" + recipe.DisplayName + "</b>  <color=#77736d>$" + DishJudge.MenuPrice(recipe, margin).ToString("0.00") + "</color>"
                   + (covered ? "  <color=#73cc73>covered</color>" : "") + "\n" + line;
        }
    }
}
