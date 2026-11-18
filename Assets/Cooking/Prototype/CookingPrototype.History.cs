using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The tablet's History page (design doc: Recipe App → Cooking history). Every order is kept as a
    /// report: what was ordered, what they received (and whether it was a soup, fried or raw), the
    /// receipt, and the whole conversation, with the dish's flavour profile at the bottom.
    /// </summary>
    public partial class CookingPrototype
    {
        private enum BookPage
        {
            Recipes,
            History,
        }

        private BookPage bookPage = BookPage.Recipes;
        private HistoryEntry selectedEntry;

        private const string Muted = "#a6a199";

        private void SelectEntry(HistoryEntry entry)
        {
            selectedEntry = entry;
            if(bookScroll != null) bookScroll.verticalNormalizedPosition = 1f;
            RefreshAll();
        }

        private void RefreshHistoryPage()
        {
            if(selectedEntry == null || !history.Contains(selectedEntry)) selectedEntry = history.Count > 0 ? history[history.Count - 1] : null;

            RebuildHistoryList();
            bookRadar.ClearProfiles();
            bookRadar.SetZones(null);

            if(selectedEntry == null)
            {
                UIFactory.ClearChildren(bookPicture);
                bookTitle.text = "<b>Cooking history</b>\n<size=12><color=" + Muted + ">Nothing served yet.</color></size>";
                bookSteps.text = "<color=" + Muted + ">Every order you serve is kept here: what they ordered, what they got, the receipt and what they said.</color>";
                bookRadarHeader.text = "";
            }
            else
            {
                HistoryEntry entry = selectedEntry;
                BuildDishPicture(bookPicture, entry.Served);
                bookTitle.text = "<b>" + DishName(entry.Dish, "Nothing") + "</b> <size=13>for " + entry.Customer.name + "</size>\n<size=12><color=" + Muted + ">Cycle "
                                 + entry.Day + " · " + VerdictLabel(entry.Verdict) + "</color></size>";
                bookSteps.text = Report(entry);

                bookRadarHeader.text = "FLAVOUR PROFILE";
                FlavourZone landed = zones.Find(z => z != null && z.DisplayName == entry.BestZone);
                if(landed != null && FlavourEvaluator.Score(entry.Flavour, landed, Falloff) >= DishJudge.Settings.Default.ZoneAt) bookRadar.AddZone(landed, TastyZoneBlue);
                if(entry.OrderKnown) bookRadar.AddDot(entry.OrderedFlavour, OrderGrey);
                bookRadar.AddDot(entry.Flavour, UIFactory.Accent);
            }

            RefreshHistorySave(selectedEntry);
        }

        /// <summary>Every order served, newest first: who it was for and what they got.</summary>
        private void RebuildHistoryList()
        {
            UIFactory.ClearChildren(bookList);

            if(history.Count == 0)
            {
                Text none = UIFactory.Label(bookList, "<color=" + Muted + ">No orders yet.</color>", 13);
                UIFactory.Size(none, 30f, 0f);
                return;
            }

            for(int i = history.Count - 1; i >= 0; i--)
            {
                HistoryEntry entry = history[i];
                string label = "<b>" + DishName(entry.Dish, "Nothing") + "</b>\n<size=11><color=" + Muted + ">" + entry.Customer.name + " · cycle " + entry.Day + "</color></size>";
                Button row = AddListButton(label, entry == selectedEntry, () => SelectEntry(entry), 46f);
                PinLeft(row, FaceIcon(row.transform, entry.Customer, 32f), 32f);
            }
        }

        /// <summary>The order's current name (it may have been named since), or the name it had then.</summary>
        private static string OrderedName(HistoryEntry entry)
        {
            return entry.Order != null ? entry.Order.DisplayName : entry.Ordered ?? "-";
        }

        /// <summary>
        /// The report for one order: what they ordered, what they received (written like the order
        /// card, and whether it was a soup, fried or raw), the receipt, and the whole conversation.
        /// </summary>
        private string Report(HistoryEntry entry)
        {
            StringBuilder sb = new StringBuilder();
            AppendOrderAndReceipt(sb, OrderedName(entry), entry.Served, entry.Price, entry.Payment + entry.Tip, entry.IngredientCost);

            sb.Append("\n<b>TRANSCRIPT</b>\n");
            if(entry.Speech.Count == 0) sb.Append("  <color=").Append(Muted).Append(">They didn't say anything.</color>\n");
            foreach(VisitEvent line in entry.Speech) sb.Append("  <i><color=#c8c3ba>\"").Append(line.Text).Append("\"</color></i>\n");

            return sb.ToString();
        }

        /// <summary>Ordered, Received (with soup / fried / raw) and the receipt: the History report and the Dish Served page.</summary>
        private static void AppendOrderAndReceipt(StringBuilder sb, string ordered, IReadOnlyList<IngredientPortion> served, float price, float received, float cost)
        {
            sb.Append("<b>ORDERED</b>\n  ").Append(ordered).Append('\n');

            sb.Append("\n<b>RECEIVED</b>");
            if(served.Count > 0) sb.Append("  <color=").Append(Muted).Append(">").Append(DishTypeOf(served)).Append("</color>");
            sb.Append('\n');
            if(served.Count == 0) sb.Append("  <color=").Append(Muted).Append(">Nothing.</color>\n");
            foreach(IngredientPortion portion in served)
            {
                sb.Append("  - ").Append(Describe(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep))).Append('\n');
            }

            float profit = received - cost;
            sb.Append("\n<b>RECEIPT</b>\n");
            sb.Append("  Dish price        $").Append(price.ToString("0.00")).Append('\n');
            sb.Append("  Cash received   $").Append(received.ToString("0.00")).Append('\n');
            sb.Append("  Ingredients       $").Append(cost.ToString("0.00")).Append('\n');
            sb.Append("  <color=").Append(Muted).Append(">--------------------------</color>\n");
            sb.Append("  <b>Profit</b>             <color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color>\n");
        }

        private static string VerdictLabel(Verdict verdict)
        {
            switch(verdict)
            {
                case Verdict.DreamMeal: return "<color=#f28c33>Dream meal</color>";
                case Verdict.Ordered: return "<color=#73cc73>Correct order</color>";
                case Verdict.VeryClose: return "<color=#73cc73>Wrong order, good food (liked)</color>";
                case Verdict.CloseEnough: return "<color=#f28c33>Wrong order, good food</color>";
                case Verdict.Refused: return "<color=#e5594d>Wrong order, bad food (refused)</color>";
                default: return "<color=#e5594d>Wrong order, bad food</color>";
            }
        }
    }
}
