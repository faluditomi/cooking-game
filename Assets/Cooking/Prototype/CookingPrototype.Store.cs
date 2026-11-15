using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>The Store area: buy the day's ingredients in the morning. Closed during service.</summary>
    public partial class CookingPrototype
    {
        private class StoreRow
        {
            public IngredientDefinition Ingredient;
            public Text Owned;
            public Button BuyOne;
            public Button BuyFive;
        }

        private readonly List<StoreRow> storeRows = new List<StoreRow>();
        private Text storePlanText;

        private void Buy(IngredientDefinition ingredient, int count)
        {
            if(phase != Phase.Morning)
            {
                Say("The store is closed until tomorrow morning.", UIFactory.Bad);
                return;
            }

            int affordable = ingredient.Price <= 0f ? count : Mathf.Min(count, Mathf.FloorToInt(money / ingredient.Price + 0.0001f));

            if(affordable <= 0)
            {
                Say("Can't afford " + ingredient.DisplayName + ".", UIFactory.Bad);
                return;
            }

            float cost = ingredient.Price * affordable;
            money -= cost;
            spentToday += cost;
            inventory.Add(ingredient, PrepSteps.None, affordable);

            Say("Bought " + affordable + "× " + ingredient.DisplayName + " for $" + cost.ToString("0.00")
                + (affordable < count ? " (all you could afford)." : "."));
            RefreshAll();
        }

        private GameObject BuildStoreArea(RectTransform root)
        {
            RectTransform shelf = Column(root, "Shelf", 0f, 0.62f);
            VerticalLayoutGroup layout = UIFactory.Vertical(shelf.gameObject, 12f, 16);
            layout.childForceExpandHeight = false;
            Header(shelf, "STORE  <color=#77736d>open in the morning only. Everything here goes into your stock raw</color>");

            RectTransform grid = UIFactory.Rect(shelf, "Grid");
            GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(370f, 92f);
            gridLayout.spacing = new Vector2(10f, 10f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 3;
            UIFactory.Size(grid, -1f, 1f);

            foreach(IngredientDefinition ingredient in pantry)
            {
                if(ingredient == null) continue;

                IngredientDefinition captured = ingredient;
                StoreRow row = new StoreRow { Ingredient = ingredient };
                RectTransform card = UIFactory.Panel(grid, ingredient.DisplayName, UIFactory.PanelDark).rectTransform;

                Text name = UIFactory.Label(card, ingredient.DisplayName + "  <size=16><color=#a6a199>$" + ingredient.Price.ToString("0.00") + "</color></size>",
                    20, TextAnchor.UpperLeft, FontStyle.Bold);
                UIFactory.Place(name.rectTransform, 0f, 0.5f, 0.7f, 1f).offsetMin = new Vector2(12f, 0f);
                name.rectTransform.offsetMax = new Vector2(0f, -8f);

                row.Owned = UIFactory.Label(card, "", 16, TextAnchor.UpperRight, FontStyle.Normal, UIFactory.MutedText);
                UIFactory.Place(row.Owned.rectTransform, 0.6f, 0.5f, 1f, 1f).offsetMax = new Vector2(-12f, -8f);

                RectTransform buttons = UIFactory.Rect(card, "Buttons");
                UIFactory.Place(buttons, 0f, 0f, 1f, 0.5f, 8f);
                UIFactory.Horizontal(buttons.gameObject, 8f);
                row.BuyOne = UIFactory.Button(buttons, "Buy 1", () => Buy(captured, 1), null, 17);
                row.BuyFive = UIFactory.Button(buttons, "Buy 5", () => Buy(captured, 5), null, 17);

                storeRows.Add(row);
            }

            RectTransform plan = Column(root, "Planner", 0.62f, 1f);
            VerticalLayoutGroup planLayout = UIFactory.Vertical(plan.gameObject, 10f, 16);
            planLayout.childForceExpandHeight = false;
            Header(plan, "MENU  <color=#77736d>what each dish needs and what you have</color>");
            storePlanText = UIFactory.Label(plan, "", 17);

            return root.gameObject;
        }

        private void RefreshStore()
        {
            foreach(StoreRow row in storeRows)
            {
                int owned = inventory.Total(row.Ingredient);
                row.Owned.text = owned > 0 ? "In stock: " + owned : "";
                bool open = phase == Phase.Morning;
                row.BuyOne.interactable = open && money >= row.Ingredient.Price;
                row.BuyFive.interactable = open && money >= row.Ingredient.Price;
            }

            if(storePlanText != null) storePlanText.text = DescribeMenuNeeds();
        }

        /// <summary>For each dish: every ingredient as have/need, coloured by whether you have enough.</summary>
        private string DescribeMenuNeeds()
        {
            StringBuilder sb = new StringBuilder();

            foreach(RecipeDefinition recipe in menu)
            {
                if(recipe == null) continue;

                // Total needed per ingredient across the recipe's portions.
                Dictionary<IngredientDefinition, int> needs = new Dictionary<IngredientDefinition, int>();
                List<IngredientDefinition> orderOfAppearance = new List<IngredientDefinition>();

                foreach(IngredientPortion portion in recipe.Portions)
                {
                    if(portion == null || !portion.IsValid) continue;
                    if(!needs.ContainsKey(portion.ingredient)) orderOfAppearance.Add(portion.ingredient);
                    needs.TryGetValue(portion.ingredient, out int existing);
                    needs[portion.ingredient] = existing + portion.quantity;
                }

                bool canMake = true;
                StringBuilder line = new StringBuilder();

                foreach(IngredientDefinition ingredient in orderOfAppearance)
                {
                    int have = inventory.Total(ingredient);
                    int need = needs[ingredient];
                    if(have < need) canMake = false;
                    if(line.Length > 0) line.Append(",  ");
                    line.Append("<color=#").Append(have >= need ? "73cc73" : "a6a199").Append(">")
                        .Append(ingredient.DisplayName).Append(' ').Append(have).Append('/').Append(need).Append("</color>");
                }

                sb.Append("<b>").Append(recipe.DisplayName).Append("</b>  ")
                  .Append(canMake ? "<color=#73cc73>can make one</color>" : "")
                  .Append("  <color=#77736d>$").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00")).Append("</color>\n")
                  .Append(line).Append("\n\n");
            }

            sb.Append(DescribeRegulars());
            return sb.ToString();
        }
    }
}
