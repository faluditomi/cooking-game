using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The recipe book's History page (design doc: Recipe App → Cooking history): every dish you've
    /// served, who for, how it went, what they said, and the clues towards each regular's secret
    /// desire. Filter by regular to see whether you're getting closer.
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

        private void ShowBookPage(BookPage page)
        {
            bookPage = page;
            RefreshAll();
        }

        private void FilterHistory(Customer regular)
        {
            historyFilter = regular;
            RefreshAll();
        }

        private void RefreshHistoryPage()
        {
            RebuildHistoryFilters();

            List<HistoryEntry> entries = history.FindAll(e => historyFilter == null || e.Customer == historyFilter);
            bookTitle.text = "<b>Cooking history</b>\n<size=18><color=#a6a199>"
                             + (historyFilter == null ? "Everyone" : historyFilter.name) + ", newest first. "
                             + entries.Count + (entries.Count == 1 ? " dish." : " dishes.") + "</color></size>";

            if(entries.Count == 0)
            {
                bookSteps.text = "<color=#a6a199>Nothing served yet.</color>";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                for(int i = entries.Count - 1; i >= 0; i--) AppendEntry(sb, entries[i]);
                bookSteps.text = sb.ToString();
            }

            RefreshHistorySide();
        }

        private void RebuildHistoryFilters()
        {
            UIFactory.ClearChildren(bookList);
            AddFilterButton("Everyone", null);

            foreach(Customer regular in customers)
            {
                if(regular != null) AddFilterButton(regular.name + "  <size=15><color=#a6a199>" + StageLabel(Relationship(regular).Stage) + "</color></size>", regular);
            }
        }

        private void AddFilterButton(string label, Customer regular)
        {
            Button button = UIFactory.Button(bookList, label, () => FilterHistory(regular),
                historyFilter == regular ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor, 18);
            Text text = button.GetComponentInChildren<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.rectTransform.offsetMin = new Vector2(12f, 0f);
            UIFactory.Size(button, 42f, 0f);
        }

        private static void AppendEntry(StringBuilder sb, HistoryEntry entry)
        {
            sb.Append("<b>Day ").Append(entry.Day).Append(" · ").Append(entry.Customer.name).Append("</b>  <color=#a6a199>")
              .Append(StageLabelStatic(entry.Stage)).Append(" · ordered ").Append(entry.Ordered).Append("</color>\n");

            List<string> served = new List<string>();
            foreach(IngredientPortion portion in entry.Served) served.Add(Describe(portion, true));
            sb.Append("  ").Append(string.Join(",  ", served)).Append('\n');

            sb.Append("  ").Append(VerdictLabel(entry.Verdict));
            if(entry.Verdict != Verdict.Refused && entry.Verdict != Verdict.DreamMeal && entry.Verdict != Verdict.Ordered)
            {
                sb.Append(" <color=#a6a199>(").Append((entry.Similarity * 100f).ToString("0")).Append("% like the order)</color>");
            }

            sb.Append(" · paid $").Append(entry.Paid.ToString("0.00"));
            if(entry.Late) sb.Append(" <color=#e5594d>late</color>");
            sb.Append('\n');

            foreach(string line in entry.Lines)
            {
                if(line == entry.Clue) continue;
                sb.Append("  <i><color=#c8c3ba>\"").Append(line).Append("\"</color></i>\n");
            }

            if(!string.IsNullOrEmpty(entry.Clue))
            {
                sb.Append("  <color=#f28c33>Clue: \"").Append(entry.Clue).Append("\"</color>  ").Append(WarmthLabel(entry.Warmth)).Append('\n');
            }

            sb.Append('\n');
        }

        /// <summary>Right side: the closest dish so far for the selected regular, and the clues they've given.</summary>
        private void RefreshHistorySide()
        {
            bookRadar.ClearProfiles();
            bookRadar.SetHighlight(null);

            if(historyFilter == null)
            {
                bookRadarHeader.text = "SELECT A REGULAR";
                StringBuilder all = new StringBuilder("<color=#a6a199>Pick someone on the left to see the closest you've come to what they crave.</color>\n\n");

                foreach(Customer regular in customers)
                {
                    if(regular == null || regular.secretDesire == null) continue;
                    CustomerRelationship relationship = Relationship(regular);
                    all.Append("<b>").Append(regular.name).Append("</b>: ")
                       .Append(relationship.DreamMeal != null ? "<color=#f28c33>found: " + relationship.DreamMeal.DisplayName + "</color>" : CluesHeard(regular) + " clues heard")
                       .Append('\n');
                }

                bookHistoryInfo.text = all.ToString();
                return;
            }

            CustomerRelationship selected = Relationship(historyFilter);
            HistoryEntry closest = null;

            foreach(HistoryEntry entry in history)
            {
                if(entry.Customer == historyFilter && (closest == null || entry.DesireDistance < closest.DesireDistance)) closest = entry;
            }

            bookRadarHeader.text = "CLOSEST DISH SO FAR";
            if(closest != null) bookRadar.AddProfile(closest.Flavour, new Color(0.95f, 0.93f, 0.86f, 1f), true);

            StringBuilder sb = new StringBuilder();

            if(selected.DreamMeal != null)
            {
                sb.Append("<color=#f28c33><b>Found it: ").Append(selected.DreamMeal.DisplayName).Append("</b></color>\n");
            }
            else if(historyFilter.secretDesire == null)
            {
                sb.Append("<color=#a6a199>They don't seem to crave anything in particular.</color>\n");
            }
            else if(closest != null)
            {
                List<string> served = new List<string>();
                foreach(IngredientPortion portion in closest.Served) served.Add(portion.quantity + "× " + portion.ingredient.DisplayName);
                sb.Append("Day ").Append(closest.Day).Append(": ").Append(string.Join(", ", served)).Append('\n');
            }

            if(selected.Stage == NpcStage.Dejected && selected.DreamMeal == null)
            {
                sb.Append("<color=#a6a199>They don't open up yet. Warm them up and they'll start dropping hints.</color>\n");
            }

            sb.Append("\n<b>Clues</b>\n");
            int clues = 0;

            foreach(HistoryEntry entry in history)
            {
                if(entry.Customer != historyFilter || string.IsNullOrEmpty(entry.Clue)) continue;
                sb.Append("  Day ").Append(entry.Day).Append(": \"").Append(entry.Clue).Append("\"\n");
                clues++;
            }

            if(clues == 0) sb.Append("  <color=#a6a199>None yet.</color>\n");
            bookHistoryInfo.text = sb.ToString();
        }

        private int CluesHeard(Customer regular)
        {
            int count = 0;
            foreach(HistoryEntry entry in history) if(entry.Customer == regular && !string.IsNullOrEmpty(entry.Clue)) count++;
            return count;
        }

        private static string StageLabelStatic(NpcStage stage)
        {
            return StageLabel(stage);
        }

        private static string VerdictLabel(Verdict verdict)
        {
            switch(verdict)
            {
                case Verdict.DreamMeal: return "<color=#f28c33>Dream meal</color>";
                case Verdict.Ordered: return "<color=#73cc73>Exact order</color>";
                case Verdict.VeryClose: return "<color=#73cc73>Very close</color>";
                case Verdict.CloseEnough: return "<color=#f28c33>Close enough</color>";
                case Verdict.Refused: return "<color=#e5594d>Refused</color>";
                default: return "<color=#e5594d>Too different</color>";
            }
        }

        private static string WarmthLabel(Warmth warmth)
        {
            switch(warmth)
            {
                case Warmth.Warmer: return "<color=#73cc73>▲ warmer</color>";
                case Warmth.Colder: return "<color=#e5594d>▼ colder</color>";
                case Warmth.Same: return "<color=#a6a199>= about the same</color>";
                default: return "";
            }
        }
    }
}
