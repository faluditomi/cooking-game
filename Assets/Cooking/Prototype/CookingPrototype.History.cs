using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The recipe book's History page (design doc: Recipe App → Cooking history). Every order is
    /// kept as a full report: what was ordered, everything said, everything cooked (in order, with
    /// times), what was served, its flavour profile next to the order's, and how it went. Filter by
    /// regular to follow your attempts at their secret desire.
    /// </summary>
    public partial class CookingPrototype
    {
        private enum BookPage
        {
            Recipes,
            History,
        }

        private BookPage bookPage = BookPage.Recipes;
        /// <summary>Regular the history is filtered to, or null for everyone.</summary>
        private Customer historyFilter;
        private HistoryEntry selectedEntry;

        private const string Muted = "#a6a199";

        private void ShowBookPage(BookPage page)
        {
            ShowTabletPage(page == BookPage.History ? TabletPage.History : TabletPage.Recipes);
        }

        private void FilterHistory(Customer regular)
        {
            historyFilter = regular;
            selectedEntry = null;
            RefreshAll();
        }

        private void SelectEntry(HistoryEntry entry)
        {
            selectedEntry = entry;
            RefreshAll();
        }

        private void RefreshHistoryPage()
        {
            List<HistoryEntry> entries = history.FindAll(e => historyFilter == null || e.Customer == historyFilter);
            if(selectedEntry == null || !entries.Contains(selectedEntry)) selectedEntry = entries.Count > 0 ? entries[entries.Count - 1] : null;

            RebuildHistoryList(entries);

            if(selectedEntry == null)
            {
                UIFactory.ClearChildren(bookPicture);
                bookTitle.text = "<b>Cooking history</b>\n<size=18><color=" + Muted + ">Nothing served yet"
                                 + (historyFilter != null ? " to " + historyFilter.name : "") + ".</color></size>";
                bookSteps.text = "<color=" + Muted + ">Every order you serve is kept here as a full report: what they ordered and said, "
                                 + "what you cooked, the flavour of the dish, and how it went.</color>";
            }
            else
            {
                HistoryEntry entry = selectedEntry;
                BuildDishPicture(bookPicture, entry.Served);
                bookTitle.text = "<b>" + DishName(entry.Dish, "Nothing") + "</b> <size=22>for " + entry.Customer.name + "</size>\n<size=18><color=" + Muted + ">Cycle "
                                 + entry.Day + " · " + VerdictLabel(entry.Verdict) + "</color></size>";
                bookSteps.text = Report(entry);
            }

            RefreshHistorySide();
            RefreshHistorySave(selectedEntry);
        }

        // ------------------------------------------------------------------ Left: filters and orders

        private void RebuildHistoryList(List<HistoryEntry> entries)
        {
            UIFactory.ClearChildren(bookList);

            Text who = Header(bookList, "WHO");
            UIFactory.Size(who, 24f, 0f);
            AddListButton("Everyone", historyFilter == null, () => FilterHistory(null), 34f);

            foreach(Customer regular in customers)
            {
                if(regular == null) continue;
                Customer captured = regular;
                Button whoButton = AddListButton(regular.name + "  <size=14><color=" + Muted + ">" + StageLabel(Relationship(regular).Stage) + "</color></size>",
                    historyFilter == regular, () => FilterHistory(captured), 40f);
                PinLeft(whoButton, FaceIcon(whoButton.transform, regular, 32f), 32f);
            }

            Text orders = Header(bookList, "ORDERS  <color=#77736d>" + entries.Count + ", newest first</color>");
            UIFactory.Size(orders, 34f, 0f);

            for(int i = entries.Count - 1; i >= 0; i--)
            {
                HistoryEntry entry = entries[i];
                // Who it was for is the face; the rest on three short lines.
                string label = "<b>" + DishName(entry.Dish, "Nothing") + "</b>\n<size=13>" + VerdictLabel(entry.Verdict) + "</size>\n<size=13><color=" + Muted
                               + ">cycle " + entry.Day + " · $" + entry.Paid.ToString("0.00") + (string.IsNullOrEmpty(entry.Clue) ? "" : " · clue") + "</color></size>";
                Button row = AddListButton(label, entry == selectedEntry, () => SelectEntry(entry), 70f);
                PinLeft(row, FaceIcon(row.transform, entry.Customer, 44f), 44f);
                PinRight(row, DishIcon(row.transform, entry.Served, 46f), 46f);
            }
        }

        private Button AddListButton(string label, bool selected, UnityEngine.Events.UnityAction onClick, float height)
        {
            Button button = UIFactory.Button(bookList, label, onClick, selected ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor, 17);
            Text text = button.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            UIFactory.FitText(text);
            UIFactory.Size(button, height, 0f);
            return button;
        }

        // ------------------------------------------------------------------ Middle: the report

        /// <summary>The order's current name (it may have been named since), or the name it had then.</summary>
        private static string OrderedName(HistoryEntry entry)
        {
            return entry.Order != null ? entry.Order.DisplayName : entry.Ordered ?? "-";
        }

        /// <summary>The full report for one order.</summary>
        /// <summary>
        /// The report for one order: what they ordered, what they received (written like the order
        /// card), the receipt, and the whole conversation.
        /// </summary>
        private string Report(HistoryEntry entry)
        {
            StringBuilder sb = new StringBuilder();

            sb.Append("<b>ORDERED</b>\n  ").Append(OrderedName(entry)).Append('\n');

            sb.Append("\n<b>RECEIVED</b>\n");
            if(entry.Served.Count == 0) sb.Append("  <color=").Append(Muted).Append(">Nothing.</color>\n");
            foreach(IngredientPortion portion in entry.Served)
            {
                sb.Append("  - ").Append(Describe(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep))).Append('\n');
            }

            float received = entry.Payment + entry.Tip;
            float profit = received - entry.IngredientCost;
            sb.Append("\n<b>RECEIPT</b>\n");
            sb.Append("  Dish price          $").Append(entry.Price.ToString("0.00")).Append('\n');
            sb.Append("  Cash received    $").Append(received.ToString("0.00")).Append('\n');
            sb.Append("  Ingredients        $").Append(entry.IngredientCost.ToString("0.00")).Append('\n');
            sb.Append("  <color=").Append(Muted).Append(">--------------------------------</color>\n");
            sb.Append("  <b>Profit</b>               <color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color>\n");

            sb.Append("\n<b>TRANSCRIPT</b>\n");
            if(entry.Speech.Count == 0) sb.Append("  <color=").Append(Muted).Append(">They didn't say anything.</color>\n");
            foreach(VisitEvent line in entry.Speech) sb.Append("  <i><color=#c8c3ba>\"").Append(line.Text).Append("\"</color></i>\n");

            return sb.ToString();
        }

        private static string AxisCell(HistoryEntry entry, FlavourAxis axis)
        {
            float served = entry.Flavour[axis];
            if(!entry.OrderKnown) return (FlavourAxes.Label(axis) + " ").PadRight(11) + "<b>" + served.ToString("0.0") + "</b>";

            float ordered = entry.OrderedFlavour[axis];
            float diff = served - ordered;
            string color = Mathf.Abs(diff) < 0.75f ? "#ebe6db" : diff > 0f ? "#f28c33" : "#7fb2e5";
            return (FlavourAxes.Label(axis) + " ").PadRight(11) + "<color=" + color + "><b>" + served.ToString("0.0") + "</b></color> <color=" + Muted + ">(" + ordered.ToString("0.0") + ")</color>";
        }

        private static string Stamp(float seconds)
        {
            return "[" + FormatTime(seconds) + "]";
        }

        // ------------------------------------------------------------------ Right: the dish's flavour

        private void RefreshHistorySide()
        {
            bookRadar.ClearProfiles();
            StringBuilder sb = new StringBuilder();

            if(selectedEntry != null)
            {
                bookRadarHeader.text = "FLAVOUR OF THIS DISH";
                if(selectedEntry.OrderKnown) bookRadar.AddProfile(selectedEntry.OrderedFlavour, new Color(0.6f, 0.6f, 0.6f, 0.8f), false);
                bookRadar.AddProfile(selectedEntry.Flavour, new Color(0.95f, 0.55f, 0.2f, 1f), true);

                if(selectedEntry.OrderKnown)
                {
                    sb.Append("<color=#f28c33>Orange</color>: what you served.  <color=#999999>Grey outline</color>: what they ordered.\n");
                    sb.Append("<color=").Append(Muted).Append(">In the profile, orange is more than the order, blue is less.</color>\n\n");
                }
                else
                {
                    sb.Append("<color=#f28c33>Orange</color>: what you served. They ordered something that isn't in your book.\n\n");
                }
            }
            else
            {
                bookRadarHeader.text = "FLAVOUR";
            }

            Customer regular = historyFilter ?? selectedEntry?.Customer;
            if(regular != null) AppendDesireProgress(sb, regular);
            bookHistoryInfo.text = sb.ToString();
        }

        /// <summary>What you know so far about a regular's secret desire.</summary>
        private void AppendDesireProgress(StringBuilder sb, Customer regular)
        {
            CustomerRelationship relationship = Relationship(regular);
            sb.Append("<b>").Append(regular.name).Append("</b>\n");

            if(relationship.DreamMeal != null)
            {
                sb.Append("<color=#f28c33>Found it: ").Append(DishName(relationship.DreamMeal)).Append("</color>\n");
                return;
            }

            if(regular.secretDesire == null)
            {
                sb.Append("<color=").Append(Muted).Append(">They don't seem to crave anything in particular.</color>\n");
                return;
            }

            if(relationship.Stage == NpcStage.Stranger)
            {
                sb.Append("<color=").Append(Muted).Append(">They don't open up yet. Once they're familiar they'll start dropping hints.</color>\n");
            }

            int clues = 0;
            foreach(HistoryEntry entry in history)
            {
                if(entry.Customer != regular || string.IsNullOrEmpty(entry.Clue)) continue;
                if(clues == 0) sb.Append("Clues so far:\n");
                sb.Append("  Cycle ").Append(entry.Day).Append(": \"").Append(entry.Clue).Append("\"\n");
                clues++;
            }

            if(clues == 0 && relationship.Stage != NpcStage.Stranger) sb.Append("<color=").Append(Muted).Append(">No clues yet.</color>\n");
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

        private static string WarmthLabel(Warmth warmth)
        {
            switch(warmth)
            {
                case Warmth.Warmer: return "<color=#73cc73>(warmer)</color>";
                case Warmth.Colder: return "<color=#e5594d>(colder)</color>";
                case Warmth.Same: return "<color=#a6a199>= about the same</color>";
                default: return "";
            }
        }
    }
}
