using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The morning market: a grocer's stall of its own, over the whole stall view. Ingredients sit on
    /// shelves with price tags naming them; clicking one, or dragging it into the basket, puts a unit
    /// in the basket, where everything picked is heaped. Click a piece in the basket, or drag it out,
    /// to put one back. Recipe bundles add everything one dish
    /// needs. Nothing is paid until "Buy and start the day", which pays for the basket and opens the
    /// stall.
    /// </summary>
    public partial class CookingPrototype
    {
        private class ShelfItem
        {
            public IngredientDefinition Ingredient;
            public Text Stock;
        }

        private const int ShelfColumns = 5;

        private readonly ShoppingCart cart = new ShoppingCart();
        private readonly List<ShelfItem> shelfItems = new List<ShelfItem>();
        private GameObject storeScene;
        private RectTransform basketInside;
        private RectTransform bundleGrid;
        /// <summary>The menu names the bundle tiles were built for, to rebuild them when it changes.</summary>
        private string bundleKey;
        /// <summary>What the basket heap was drawn for, to redraw it only when it changes.</summary>
        private string basketKey;
        private Text basketTotalText;
        private Text storeMoneyText;
        private Button clearCartButton;
        private Button dayTimerButton;
        private Text regularsText;
        private RectTransform basketArea;
        /// <summary>What's being dragged in the shop, or null.</summary>
        private IngredientDefinition shopHeld;
        /// <summary>True when the dragged piece came out of the basket (dropping it elsewhere puts it back on the shelf).</summary>
        private bool shopHeldFromBasket;

        // ------------------------------------------------------------------ Actions

        private bool StoreOpen()
        {
            if(phase == Phase.Morning) return true;
            Say("The market only sells in the morning.", UIFactory.Bad);
            return false;
        }

        private void AddToCart(IngredientDefinition ingredient, int quantity)
        {
            if(!StoreOpen()) return;
            cart.Add(ingredient, quantity);
            Play(sounds.pickup, 0.5f, 0.08f);
            RefreshAll();
        }

        private void RemoveFromCart(IngredientDefinition ingredient)
        {
            cart.Remove(ingredient);
            Play(sounds.place, 0.4f, 0.08f);
            RefreshAll();
        }

        private void AddServingToCart(RecipeDefinition recipe)
        {
            if(!StoreOpen()) return;
            cart.AddRecipe(recipe);
            Play(sounds.pickup, 0.6f, 0.05f);
            RefreshAll();
        }

        private void ClearCart()
        {
            cart.Clear();
            RefreshAll();
        }

        // ------------------------------------------------------------------ Dragging

        private void BeginShopDrag(IngredientDefinition ingredient, Vector2 screen, bool fromBasket)
        {
            if(!StoreOpen()) return;

            shopHeld = ingredient;
            shopHeldFromBasket = fromBasket;
            CreateGhost(screen, 80f, 80f);
            Image piece = UIFactory.Panel(ghost, "Piece", Color.white);
            piece.sprite = ingredient.Icon;
            piece.preserveAspect = true;
            piece.raycastTarget = false;
            UIFactory.Stretch(piece.rectTransform);
            Play(sounds.pickup, 0.5f, 0.08f);
        }

        /// <summary>Per frame: the dragged piece follows the pointer; letting go over the basket buys it, anywhere else puts it back.</summary>
        private void UpdateShopDrag()
        {
            if(shopHeld == null) return;

            UnityEngine.InputSystem.Pointer pointer = UnityEngine.InputSystem.Pointer.current;
            Vector2 position = pointer != null ? pointer.position.ReadValue() : Vector2.zero;

            if(pointer != null && pointer.press.isPressed)
            {
                MoveGhost(position);
                return;
            }

            IngredientDefinition dropped = shopHeld;
            shopHeld = null;
            DestroyGhost();
            bool overBasket = basketArea != null && RectTransformUtility.RectangleContainsScreenPoint(basketArea, position, null);

            if(!shopHeldFromBasket && overBasket) AddToCart(dropped, 1);
            else if(shopHeldFromBasket && !overBasket) RemoveFromCart(dropped);
        }

        // ------------------------------------------------------------------ UI

        /// <summary>The grocer's stall: shelves on the left with the bundles under them, the basket on the right.</summary>
        private void BuildStoreScene(Transform root)
        {
            Image back = UIFactory.Panel(root, "Market Store", new Color(0.13f, 0.1f, 0.14f, 1f));
            UIFactory.Place(back.rectTransform, ConversationWidth, 0f, 1f, 1f);
            storeScene = back.gameObject;
            RectTransform scene = back.rectTransform;

            // A lit sign over the stall.
            Image sign = UIFactory.Panel(scene, "Sign", new Color(0.2f, 0.12f, 0.1f, 1f));
            UIFactory.SkinPanel(sign, new Color(0.24f, 0.14f, 0.11f, 1f));
            UIFactory.Place(sign.rectTransform, 0.02f, 0.92f, 0.66f, 0.99f);
            Text signText = UIFactory.Label(sign.transform, "GROCER  <size=18><color=#c9b38a>· Bright Market ·  click or drag into the basket</color></size>", 30,
                TextAnchor.MiddleCenter, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Stretch(signText.rectTransform, 4f);

            BuildShelves(scene);
            BuildBundles(scene);
            BuildBasket(scene);
            storeScene.SetActive(false);
        }

        private void BuildShelves(RectTransform scene)
        {
            RectTransform wall = UIFactory.Panel(scene, "Shelves", new Color(0.2f, 0.15f, 0.13f, 1f)).rectTransform;
            UIFactory.Place(wall, 0.02f, 0.3f, 0.66f, 0.91f);

            List<IngredientDefinition> stocked = pantry.FindAll(i => i != null);
            int shelves = Mathf.Max(1, (stocked.Count + ShelfColumns - 1) / ShelfColumns);

            for(int s = 0; s < shelves; s++)
            {
                float top = 1f - s / (float)shelves;
                float bottom = 1f - (s + 1) / (float)shelves;

                // The plank the items stand on.
                Image plank = UIFactory.Panel(wall, "Plank", new Color(0.45f, 0.3f, 0.18f, 1f));
                plank.raycastTarget = false;
                UIFactory.Place(plank.rectTransform, 0.01f, bottom + 0.015f, 0.99f, bottom + 0.045f);
                Image lip = UIFactory.Panel(plank.transform, "Edge", new Color(0.3f, 0.19f, 0.11f, 1f));
                lip.raycastTarget = false;
                UIFactory.Place(lip.rectTransform, 0f, 0f, 1f, 0.35f);

                for(int c = 0; c < ShelfColumns; c++)
                {
                    int index = s * ShelfColumns + c;
                    if(index >= stocked.Count) break;

                    RectTransform slot = UIFactory.Rect(wall, stocked[index].DisplayName);
                    UIFactory.Place(slot, c / (float)ShelfColumns, bottom + 0.045f, (c + 1) / (float)ShelfColumns, top - 0.01f, 4f);
                    BuildShelfItem(slot, stocked[index]);
                }
            }
        }

        /// <summary>A few of the ingredient standing on the shelf, its price on a tag, and how many you have at home.</summary>
        private void BuildShelfItem(RectTransform slot, IngredientDefinition ingredient)
        {
            IngredientDefinition captured = ingredient;
            Image hit = slot.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Button button = slot.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.onClick.AddListener(() => AddToCart(captured, 1));
            slot.gameObject.AddComponent<DragHandle>().Began = screen => BeginShopDrag(captured, screen, false);

            AddPile(slot, ingredient.Icon, 3, 56f, new Vector2(0.5f, 0.64f));

            // The price tag names it too.
            RectTransform tag = PaperNote(slot, ingredient.DisplayName + "  <b>$" + ingredient.Price.ToString("0.00") + "</b>", 13,
                ((ingredient.DisplayName.Length % 3) - 1) * 3f);
            tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0f);
            tag.pivot = new Vector2(0.5f, 0f);
            tag.sizeDelta = new Vector2(Mathf.Min(176f, 58f + ingredient.DisplayName.Length * 7.5f), 24f);
            tag.anchoredPosition = new Vector2(0f, 8f);

            Text stock = UIFactory.Label(slot, "", 11, TextAnchor.UpperRight, FontStyle.Normal, UIFactory.MutedText);
            stock.raycastTarget = false;
            UIFactory.Place(stock.rectTransform, 0f, 0.82f, 1f, 1f);
            shelfItems.Add(new ShelfItem { Ingredient = ingredient, Stock = stock });
        }

        private void BuildBundles(RectTransform scene)
        {
            Image board = UIFactory.Panel(scene, "Bundles", new Color(0.17f, 0.13f, 0.12f, 1f));
            UIFactory.Place(board.rectTransform, 0.02f, 0.02f, 0.66f, 0.28f);
            VerticalLayoutGroup layout = UIFactory.Vertical(board.gameObject, 4f, 8);
            layout.childForceExpandHeight = false;
            Text header = UIFactory.Label(board.transform, "RECIPE BUNDLES  <color=#77736d>everything one serving needs</color>", 14, TextAnchor.MiddleLeft,
                FontStyle.Bold, UIFactory.MutedText);
            UIFactory.Size(header, 20f, 0f);

            bundleGrid = UIFactory.Rect(board.transform, "Grid");
            GridLayoutGroup grid = bundleGrid.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(316f, 112f);
            grid.spacing = new Vector2(6f, 6f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
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
            bundleTiles.Clear();

            // Two rows fit; the rest of the menu is still on the shelves.
            GridLayoutGroup grid = bundleGrid.GetComponent<GridLayoutGroup>();
            int columns = Mathf.Max(3, (visible.Count + 1) / 2);
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(Mathf.Floor(940f / columns) - 6f, 74f);

            foreach(RecipeDefinition recipe in visible)
            {
                RecipeDefinition captured = recipe;
                List<string> parts = new List<string>();
                foreach(KeyValuePair<IngredientDefinition, int> need in ShoppingCart.Needs(recipe)) parts.Add(need.Value + " " + need.Key.DisplayName);

                string label = "<b>" + recipe.DisplayName + "</b>  <size=11><color=#a6a199>$" + recipe.CalculateIngredientCost().ToString("0.00")
                               + "</color></size>\n<size=10><color=#c9c4bb>" + string.Join(", ", parts) + "</color></size>";
                Button button = UIFactory.Button(bundleGrid, label, () => AddServingToCart(captured), new Color(0.24f, 0.27f, 0.36f, 1f), 13);
                Text text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                UIFactory.FitText(text);
                PinLeft(button, DishIcon(button.transform, recipe.Portions, 50f), 50f);
                bundleTiles[recipe] = (RectTransform)button.transform;
            }
        }

        /// <summary>The basket, with everything picked heaped in it, and the till under it.</summary>
        private void BuildBasket(RectTransform scene)
        {
            RectTransform column = UIFactory.Rect(scene, "Basket Side");
            UIFactory.Place(column, 0.68f, 0.02f, 0.98f, 0.99f);

            storeMoneyText = UIFactory.Label(column, "", 22, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);
            UIFactory.Place(storeMoneyText.rectTransform, 0f, 0.93f, 1f, 1f);

            // The basket: a handle over a woven body; what's picked sits inside.
            Image handle = UIFactory.Panel(column, "Handle", new Color(0.5f, 0.34f, 0.18f, 1f));
            handle.sprite = UIFactory.Circle;
            handle.raycastTarget = false;
            UIFactory.Place(handle.rectTransform, 0.14f, 0.62f, 0.86f, 0.92f);
            Image hole = UIFactory.Panel(handle.transform, "Hole", new Color(0.13f, 0.1f, 0.14f, 1f));
            hole.sprite = UIFactory.Circle;
            hole.raycastTarget = false;
            UIFactory.Place(hole.rectTransform, 0.06f, 0.08f, 0.94f, 0.92f);

            Image body = UIFactory.Panel(column, "Basket", new Color(0.62f, 0.43f, 0.22f, 1f));
            UIFactory.SkinPanel(body, new Color(0.62f, 0.43f, 0.22f, 1f));
            body.raycastTarget = false;
            UIFactory.Place(body.rectTransform, 0.04f, 0.38f, 0.96f, 0.76f);
            for(int i = 1; i < 4; i++)
            {
                Image weave = UIFactory.Panel(body.transform, "Weave", new Color(0.45f, 0.3f, 0.15f, 0.8f));
                weave.raycastTarget = false;
                UIFactory.Place(weave.rectTransform, 0.03f, i * 0.25f - 0.015f, 0.97f, i * 0.25f + 0.015f);
            }

            basketArea = body.rectTransform;
            basketInside = UIFactory.Rect(column, "In The Basket");
            UIFactory.Place(basketInside, 0.08f, 0.42f, 0.92f, 0.84f);

            basketTotalText = UIFactory.Label(column, "", 17, TextAnchor.UpperLeft);
            UIFactory.Place(basketTotalText.rectTransform, 0f, 0.29f, 1f, 0.37f);

            RectTransform buttons = UIFactory.Rect(column, "Till");
            UIFactory.Place(buttons, 0f, 0.13f, 1f, 0.29f);
            VerticalLayoutGroup layout = UIFactory.Vertical(buttons.gameObject, 6f, 0);
            layout.childForceExpandHeight = false;
            phaseButton = UIFactory.Button(UIFactory.Row(buttons, 48f), "Buy and start the day", OpenStall, Orange, 20);
            RectTransform small = UIFactory.Row(buttons, 34f, 6f);
            clearCartButton = UIFactory.Button(small, "Empty basket", ClearCart, new Color(0.35f, 0.2f, 0.2f, 1f), 14);
            dayTimerButton = UIFactory.Button(small, "", ToggleDayTimer, null, 14);

            regularsText = UIFactory.Label(column, "", 13);
            UIFactory.Place(regularsText.rectTransform, 0f, 0f, 1f, 0.13f);
        }

        private void RefreshStore()
        {
            if(storeScene == null) return;

            bool open = phase == Phase.Morning;
            storeScene.SetActive(open);
            if(!open) return;

            foreach(ShelfItem item in shelfItems)
            {
                int stock = inventory.Total(item.Ingredient);
                int inCart = cart.Count(item.Ingredient);
                item.Stock.text = (stock > 0 ? "have " + stock : "") + (inCart > 0 ? "  <color=#f28c33>+" + inCart + "</color>" : "");
            }

            RebuildBundles();
            RebuildBasket();

            float total = cart.TotalCost;
            bool affordable = total <= money + 0.0001f;
            storeMoneyText.text = "$" + money.ToString("0.00");
            basketTotalText.text = cart.IsEmpty
                ? "<color=#a6a199>The basket is empty.</color>"
                : "Total <b>$" + total.ToString("0.00") + "</b>  <color=#a6a199>(" + cart.TotalUnits + " items)</color>\n"
                  + (affordable
                      ? "<color=#a6a199>You'll have $" + (money - total).ToString("0.00") + " left.</color>"
                      : "<color=#e5594d>$" + (total - money).ToString("0.00") + " more than you have.</color>");
            UIFactory.SetButtonText(dayTimerButton, dayTimer ? "Day timer: " + FormatTime(daySeconds) : "Day timer: off");
            regularsText.text = DescribeRegulars().TrimStart('\n') + "<color=#a6a199>Rent at the end of the cycle: $" + stallRent.ToString("0.00")
                                + ". Prepped stock spoils overnight; raw keeps.</color>";
            clearCartButton.interactable = !cart.IsEmpty;
        }

        /// <summary>Each unit in the basket, heaped; clicking one puts one of those back.</summary>
        private void RebuildBasket()
        {
            List<ShoppingCart.Line> lines = cart.Lines();
            StringBuilder key = new StringBuilder();
            foreach(ShoppingCart.Line line in lines) key.Append(line.Ingredient.GetHashCode()).Append('x').Append(line.Quantity).Append('|');
            if(key.ToString() == basketKey) return;

            basketKey = key.ToString();
            UIFactory.ClearChildren(basketInside);

            List<IngredientDefinition> pieces = new List<IngredientDefinition>();
            foreach(ShoppingCart.Line line in lines) for(int i = 0; i < line.Quantity && pieces.Count < 60; i++) pieces.Add(line.Ingredient);

            // Bottom up, a little jumbled, so it looks piled in.
            float size = pieces.Count > 30 ? 46f : 58f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt(basketInside.rect.width > 0f ? basketInside.rect.width / (size * 0.75f) : 7f));

            for(int i = 0; i < pieces.Count; i++)
            {
                IngredientDefinition ingredient = pieces[i];
                Image piece = UIFactory.Panel(basketInside, ingredient.DisplayName, Color.white);
                piece.sprite = ingredient.Icon;
                piece.preserveAspect = true;
                RectTransform rt = piece.rectTransform;
                int row = i / perRow;
                int col = i % perRow;
                float jitter = ((i * 37) % 11 - 5) * 1.5f;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2(size * 0.5f + col * size * 0.75f + (row % 2) * size * 0.35f, size * 0.45f + row * size * 0.45f + jitter);
                rt.localRotation = Quaternion.Euler(0f, 0f, ((i * 53) % 31) - 15f);
                Button button = piece.gameObject.AddComponent<Button>();
                button.targetGraphic = piece;
                button.onClick.AddListener(() => RemoveFromCart(ingredient));
                piece.gameObject.AddComponent<DragHandle>().Began = screen => BeginShopDrag(ingredient, screen, true);
            }
        }
    }
}
