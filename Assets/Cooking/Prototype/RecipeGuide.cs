using System;
using System.Collections.Generic;
using System.Text;

namespace Cooking.Prototype
{
    /// <summary>
    /// Writes a recipe as steps for the player: what to buy, what to prep with which tool, which
    /// station cooks what and how, and serving. Used by the recipe book. Rich text for uGUI.
    /// </summary>
    public static class RecipeGuide
    {
        private const string Muted = "#a6a199";

        /// <param name="have">Units of an ingredient you have (stock, cart...), shown next to what to buy. Null to skip.</param>
        public static string Steps(RecipeDefinition recipe, Func<IngredientDefinition, int> have = null)
        {
            if(recipe == null) return "";

            StringBuilder sb = new StringBuilder();
            List<IngredientPortion> portions = new List<IngredientPortion>();
            foreach(IngredientPortion portion in recipe.Portions) if(portion != null && portion.IsValid) portions.Add(portion);

            // 1. Buy
            sb.Append("<b>1. Buy</b>  <color=").Append(Muted).Append(">everything comes raw from the store</color>\n");

            foreach(KeyValuePair<IngredientDefinition, int> need in ShoppingCart.Needs(recipe))
            {
                sb.Append("  • ").Append(need.Value).Append("× ").Append(need.Key.DisplayName);

                if(have != null)
                {
                    int owned = have(need.Key);
                    sb.Append("  <color=").Append(owned >= need.Value ? "#73cc73" : Muted).Append(">have ").Append(owned).Append("</color>");
                }

                sb.Append('\n');
            }

            // 2. Prep
            sb.Append("\n<b>2. Prep</b>  <color=").Append(Muted).Append(">on the prep board, one unit at a time</color>\n");
            List<string> asTheyAre = new List<string>();
            bool anyPrep = false;

            foreach(IngredientPortion portion in portions)
            {
                if(portion.prep == PrepSteps.None)
                {
                    asTheyAre.Add(portion.ingredient.DisplayName);
                    continue;
                }

                anyPrep = true;
                sb.Append("  • ").Append(portion.quantity).Append("× ").Append(portion.ingredient.DisplayName).Append(": ")
                  .Append(DescribePrep(portion.prep)).Append('\n');
            }

            if(!anyPrep) sb.Append("  Nothing to prep.\n");
            if(asTheyAre.Count > 0) sb.Append("  <color=").Append(Muted).Append(">As they are: ").Append(string.Join(", ", Distinct(asTheyAre))).Append("</color>\n");

            // 3. Cook
            sb.Append("\n<b>3. Cook</b>\n");
            List<IngredientPortion> fried = new List<IngredientPortion>();
            List<IngredientPortion> boiled = new List<IngredientPortion>();
            List<IngredientPortion> raw = new List<IngredientPortion>();

            foreach(IngredientPortion portion in portions)
            {
                if((portion.cooking & CookMethods.Boiled) != 0) boiled.Add(portion);
                if((portion.cooking & CookMethods.Fried) != 0) fried.Add(portion);
                if(portion.IsRaw) raw.Add(portion);
            }

            if(boiled.Count > 0)
            {
                IngredientPortion stock = boiled.Find(p => p.ingredient.IsStock);
                sb.Append("  <b>Pot</b>: ").Append(List(boiled)).Append(".\n    ");

                if(stock != null)
                {
                    sb.Append("Select them all and send them in together: the ").Append(stock.ingredient.DisplayName).Append(" makes it a soup.");
                }
                else if(DistinctIngredients(boiled) > 1)
                {
                    sb.Append("No stock cube, so the pot takes one ingredient at a time: boil each one separately.");
                }
                else
                {
                    sb.Append("Send it to the pot.");
                }

                sb.Append(" Start, and take it off on the green line.\n");
            }

            if(fried.Count > 0)
            {
                sb.Append("  <b>Wok</b>: ").Append(List(fried)).Append(".\n    ")
                  .Append("Select them all and send them to the wok together. Start, take it off in the green, and shake it in the red so it doesn't burn.\n");
            }

            if(boiled.Count > 0 && fried.Count > 0 && boiled.Exists(p => fried.Contains(p)))
            {
                sb.Append("  <color=").Append(Muted).Append(">Some of it is boiled and then fried: pot first, then the wok.</color>\n");
            }

            if(raw.Count > 0) sb.Append("  <b>Raw</b>: ").Append(List(raw)).Append(" straight onto the plate.\n");

            // 4. Serve
            sb.Append("\n<b>4. Serve</b>\n  Everything on one plate, then Serve. Badly cooked parts don't count as the order.");
            return sb.ToString();
        }

        /// <summary>"peel (peeler), then slice (knife)" in the order the design doc writes them.</summary>
        public static string DescribePrep(PrepSteps prep)
        {
            List<string> steps = new List<string>();
            if((prep & Cooking.PrepSteps.Peeled) != 0) steps.Add("peel <color=" + Muted + ">(peeler)</color>");
            if((prep & Cooking.PrepSteps.Sliced) != 0) steps.Add("slice <color=" + Muted + ">(knife)</color>");
            if((prep & Cooking.PrepSteps.Grated) != 0) steps.Add("grate <color=" + Muted + ">(grater)</color>");
            return string.Join(", then ", steps);
        }

        private static string List(List<IngredientPortion> portions)
        {
            List<string> items = new List<string>();

            foreach(IngredientPortion portion in portions)
            {
                string item = portion.quantity + "× " + portion.ingredient.DisplayName;
                if(portion.prep != Cooking.PrepSteps.None) item += " <color=" + Muted + ">(prepped)</color>";
                items.Add(item);
            }

            return string.Join(", ", items);
        }

        private static int DistinctIngredients(List<IngredientPortion> portions)
        {
            HashSet<IngredientDefinition> seen = new HashSet<IngredientDefinition>();
            foreach(IngredientPortion portion in portions) seen.Add(portion.ingredient);
            return seen.Count;
        }

        private static List<string> Distinct(List<string> names)
        {
            List<string> result = new List<string>();
            foreach(string name in names) if(!result.Contains(name)) result.Add(name);
            return result;
        }
    }
}
