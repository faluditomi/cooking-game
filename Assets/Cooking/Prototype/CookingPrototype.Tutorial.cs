using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The tutorial: a practice day before cycle 1. It walks through one whole loop with one simple
    /// dish (Soup on the rock: buy the bundle, peel a potato, fill the pot, dish up, serve)
    /// for one customer, with no clock and no rent. Each step waits for the player to actually do it,
    /// with a pulsing outline on what to use. Afterwards (or when skipped) cycle 1 starts fresh, so the
    /// player shops and preps for the real day themselves.
    /// </summary>
    public partial class CookingPrototype
    {
        private const string TutorialDish = "Soup on the rock";

        private enum TutorialStep
        {
            None,
            Welcome,
            Bundle,
            StartDay,
            WaitCustomer,
            Order,
            BoardPotato,
            Peel,
            FillPot,
            Plate,
            DishUp,
            Serve,
            Result,
            Done,
        }

        private TutorialStep tutorialStep;
        private RecipeDefinition tutorialRecipe;
        private Customer tutorialCustomer;
        private bool tutorialOrdered;

        private GameObject tutorialPanel;
        private Text tutorialTitle;
        private Text tutorialText;
        private Button tutorialNext;
        private RectTransform highlight;
        private Image[] highlightBars;
        private readonly Dictionary<RecipeDefinition, RectTransform> bundleTiles = new Dictionary<RecipeDefinition, RectTransform>();
        private readonly Dictionary<IngredientDefinition, RectTransform> pantryPiles = new Dictionary<IngredientDefinition, RectTransform>();

        private bool TutorialActive => tutorialStep != TutorialStep.None;

        /// <summary>The tutorial's practice day: one customer, no clock, no rent. Cycle 1 comes after.</summary>
        private bool practiceDay;

        // ------------------------------------------------------------------ Flow

        private void StartTutorial()
        {
            tutorialRecipe = menu.Find(r => r != null && r.DisplayName == TutorialDish);

            if(tutorialRecipe == null)
            {
                Debug.LogWarning("[Cooking] The tutorial needs \"" + TutorialDish + "\" on the menu; playing without it.");
                return;
            }

            // Someone who'd order it comes first: the Man, in the design doc's cast.
            tutorialCustomer = customers.Find(c => c != null && c.orders != null && c.orders.Contains(tutorialRecipe))
                               ?? customers.Find(c => c != null);
            tutorialOrdered = false;

            // Just them today.
            practiceDay = true;
            arrivals.Clear();
            arrivals.Add(tutorialCustomer);
            todaysRegulars = 1;

            ShowTutorialStep(TutorialStep.Welcome);
            RefreshAll();
        }

        private void EndTutorial()
        {
            tutorialStep = TutorialStep.None;
            practiceDay = false;
            if(tutorialPanel != null) tutorialPanel.SetActive(false);
            if(highlight != null) highlight.gameObject.SetActive(false);
        }

        /// <summary>The scripted order for the tutorial's first customer, or null to order as usual.</summary>
        private RecipeDefinition TutorialOrderFor(Customer who)
        {
            if(!TutorialActive || tutorialOrdered || who != tutorialCustomer) return null;
            tutorialOrdered = true;
            return tutorialRecipe;
        }

        private void NextTutorialStep()
        {
            if(tutorialStep == TutorialStep.Done)
            {
                BeginRealGame();
                return;
            }

            ShowTutorialStep(tutorialStep + 1);
        }

        /// <summary>The practice day is over (or skipped): cycle 1 starts fresh, from the morning shop.</summary>
        private void BeginRealGame()
        {
            bool wasPractice = practiceDay;
            EndTutorial();

            if(wasPractice)
            {
                // Money, stock and the regulars as at the start: the practice doesn't count.
                RestartGame();
                Say("Cycle 1. Fill the cart for everyone coming today, then buy and start the day.", UIFactory.Accent);
            }
        }

        /// <summary>The practice customer has been served and has left: no summary or rent, just the last tutorial page.</summary>
        private void FinishPracticeDay()
        {
            phase = Phase.DayOver;
            customer = null;
            order = null;
            orderIn = false;
            DialogueSystem.Dialogue.StopAll();
            ClearSpeech();
            ClearKitchen();
            if(tutorialStep != TutorialStep.Done) ShowTutorialStep(TutorialStep.Done);
            RefreshAll();
        }

        /// <summary>Checks whether the current step has been done (called every frame).</summary>
        private void UpdateTutorial()
        {
            if(!TutorialActive) return;

            bool done;

            switch(tutorialStep)
            {
                case TutorialStep.Bundle: done = CartCovers(tutorialRecipe); break;
                case TutorialStep.StartDay: done = phase == Phase.Service; break;
                case TutorialStep.WaitCustomer: done = customer != null && orderIn; break;
                case TutorialStep.BoardPotato: done = boards.Exists(b => b.Units != null && IsTutorialPotato(b.Units.ingredient)); break;
                case TutorialStep.Peel: done = boards.Exists(b => b.Units != null && IsTutorialPotato(b.Units.ingredient) && (b.Units.prep & PrepSteps.Peeled) != 0); break;
                case TutorialStep.FillPot: done = PotHas(tutorialRecipe); break;
                case TutorialStep.Plate: done = deskSlots.Exists(s => s.Plate != null); break;
                case TutorialStep.DishUp: done = deskSlots.Exists(s => s.Plate != null && s.Plate.HasDish); break;
                case TutorialStep.Serve: done = resultOverlay.activeSelf; break;
                case TutorialStep.Result: done = !resultOverlay.activeSelf; break;
                default: done = false; break;
            }

            if(done)
            {
                NextTutorialStep();
                return;
            }

            // Ahead of the instructions (say, the potato straight into the pot)? Catch up to where
            // they actually are, so the tutorial never waits for a step that's already been passed.
            if(tutorialStep >= TutorialStep.BoardPotato && tutorialStep < TutorialStep.Serve)
            {
                TutorialStep reached = CookingProgress();
                if(reached > tutorialStep) ShowTutorialStep(reached);
            }
        }

        /// <summary>How far through the dish the desk shows they are: the step to show next.</summary>
        private TutorialStep CookingProgress()
        {
            bool plated = deskSlots.Exists(s => s.Plate != null && s.Plate.HasDish);
            bool plateOut = deskSlots.Exists(s => s.Plate != null);

            if(plated) return TutorialStep.Serve;
            if(!pot.IsEmpty && PotHas(tutorialRecipe)) return plateOut ? TutorialStep.DishUp : TutorialStep.Plate;
            if(!pot.IsEmpty) return TutorialStep.FillPot;
            if(boards.Exists(b => b.Units != null && IsTutorialPotato(b.Units.ingredient) && (b.Units.prep & PrepSteps.Peeled) != 0)) return TutorialStep.FillPot;
            if(boards.Exists(b => b.Units != null && IsTutorialPotato(b.Units.ingredient))) return TutorialStep.Peel;
            return TutorialStep.BoardPotato;
        }

        private bool IsTutorialPotato(IngredientDefinition ingredient)
        {
            foreach(IngredientPortion portion in tutorialRecipe.Portions)
            {
                if(portion != null && portion.ingredient == ingredient && (portion.prep & PrepSteps.Peeled) != 0) return true;
            }

            return false;
        }

        /// <summary>Stock and cart hold everything one serving of <paramref name="recipe"/> needs.</summary>
        private bool CartCovers(RecipeDefinition recipe)
        {
            foreach(KeyValuePair<IngredientDefinition, int> need in ShoppingCart.Needs(recipe))
            {
                if(inventory.Total(need.Key) + cart.Count(need.Key) < need.Value) return false;
            }

            return true;
        }

        /// <summary>Everything for <paramref name="recipe"/> is in the pot, prepped as it should be.</summary>
        private bool PotHas(RecipeDefinition recipe)
        {
            foreach(IngredientPortion need in recipe.Portions)
            {
                if(need == null || !need.IsValid) continue;
                int have = 0;
                foreach(IngredientPortion portion in pot.Contents)
                {
                    if(portion.ingredient == need.ingredient && portion.prep == need.prep) have += portion.quantity;
                }

                if(have < need.quantity) return false;
            }

            return true;
        }

        // ------------------------------------------------------------------ Steps

        private void ShowTutorialStep(TutorialStep step)
        {
            tutorialStep = step;
            tutorialPanel.SetActive(true);
            tutorialPanel.transform.SetAsLastSibling();

            string dish = tutorialRecipe.DisplayName;
            string who = tutorialCustomer != null ? tutorialCustomer.name : "Your first customer";
            bool next = false;
            string text;

            switch(step)
            {
                case TutorialStep.Welcome:
                    text = "Welcome to Emphis's stall in the Bright Market.\n\nEach cycle you buy ingredients in the morning, then cook for the regulars who come by. "
                           + "At the end of the cycle the stall rent is due.\n\nThis is a <b>practice day</b>: one customer, no clock, no rent. Let's make one dish together.";
                    next = true;
                    break;
                case TutorialStep.Bundle:
                    text = "This is the <b>Shop</b> on your tablet. Bundles add everything one dish needs.\n\nClick the <b>" + dish + "</b> bundle.";
                    break;
                case TutorialStep.StartDay:
                    text = "Nothing is paid until you press it, and on real days whatever you buy has to cover everyone who comes.\n\n"
                           + "Press <b>Buy and start the day</b>.";
                    break;
                case TutorialStep.WaitCustomer:
                    text = "The stall is open. On real days a timer runs now; today it waits for you.\n\nHere comes someone...";
                    break;
                case TutorialStep.Order:
                    text = who + " wants <b>" + dish + "</b>. What they say is on the left; the order is on the tablet: "
                           + "each ingredient, how it's prepped, and whether you have it ready.\n\nThe <b>Menu</b> tab has the full steps.";
                    next = true;
                    break;
                case TutorialStep.BoardPotato:
                    text = "The potato has to be peeled first.\n\nIn the <b>pantry</b>, hold the potato and drag it onto a <b>cutting board</b>.";
                    break;
                case TutorialStep.Peel:
                    text = "Hold the <b>peeler</b> hanging from the roof and draw half circles from the top down over the board, until its bar fills.";
                    break;
                case TutorialStep.FillPot:
                    text = "Now drag the peeled potato, the <b>soup cube</b> and both <b>salts</b> into the <b>pot</b>.\n\n"
                           + "The soup cube makes it a soup, so everything can go in together. The pot starts as soon as something is in it.";
                    break;
                case TutorialStep.Plate:
                    text = "While it cooks, take a <b>clean plate</b> from the stack and drop it on the desk.";
                    break;
                case TutorialStep.DishUp:
                    text = "Watch the pot's bar. When the marker is in the <b>orange</b>, hold the pot and drop it on the plate.\n\n"
                           + "In the blue it's undercooked; in the red it's ruined. Stirring with the <b>spoon</b> keeps it in the orange longer.";
                    break;
                case TutorialStep.Serve:
                    text = "Drag the plate onto the <b>food delivery</b> spot to serve it.";
                    break;
                case TutorialStep.Result:
                    text = "This is how it went: their reaction, what they paid, and the dish's flavour.\n\n"
                           + "A dish that isn't on the menu can be named and saved here. Press <b>Continue</b>.";
                    break;
                default:
                    text = "That's the loop! On real days every regular comes once, a timer runs, and the rent is due at the end of the cycle.\n\n"
                           + "The pan works like the pot, but you lift and shake it instead of stirring. Rice has to be boiled before it's fried: pour the pot into the pan. The regulars have favourites, and secret cravings: "
                           + "listen to them and experiment.\n\nCycle 1 starts fresh: plan the shopping for everyone.";
                    next = true;
                    break;
            }

            tutorialTitle.text = "TUTORIAL  <color=#77736d>" + ((int)step) + " of " + ((int)TutorialStep.Done) + "</color>";
            tutorialText.text = text;
            tutorialNext.gameObject.SetActive(next);
            UIFactory.SetButtonText(tutorialNext, step == TutorialStep.Done ? "Start cycle 1" : "Next");
        }

        /// <summary>What the current step points at, or null.</summary>
        private RectTransform TutorialTarget()
        {
            switch(tutorialStep)
            {
                case TutorialStep.Bundle:
                    return tutorialRecipe != null && bundleTiles.TryGetValue(tutorialRecipe, out RectTransform tile) && tile != null ? tile : null;
                case TutorialStep.StartDay: return phaseButton != null ? (RectTransform)phaseButton.transform : null;
                case TutorialStep.Order: return (RectTransform)orderPage.transform;
                case TutorialStep.BoardPotato:
                    foreach(KeyValuePair<IngredientDefinition, RectTransform> pile in pantryPiles)
                    {
                        if(pile.Value != null && IsTutorialPotato(pile.Key)) return pile.Value;
                    }

                    return pantryRect;
                case TutorialStep.Peel: return toolHooks.TryGetValue(PrepSteps.Peeled, out CanvasGroup peeler) ? (RectTransform)peeler.transform : null;
                case TutorialStep.FillPot:
                case TutorialStep.DishUp:
                    return potView != null ? potView.Rect : null;
                case TutorialStep.Plate: return plateStack;
                case TutorialStep.Serve: return deliveryRect;
                case TutorialStep.Result: return resultButton != null ? (RectTransform)resultButton.transform : null;
                default: return null;
            }
        }

        /// <summary>Keeps the pulsing outline on the current step's target.</summary>
        private void UpdateTutorialHighlight()
        {
            if(highlight == null) return;

            RectTransform target = TutorialActive ? TutorialTarget() : null;
            bool show = target != null && target.gameObject.activeInHierarchy && held == Held.None;
            highlight.gameObject.SetActive(show);
            if(!show) return;

            Vector3[] corners = new Vector3[4];
            target.GetWorldCorners(corners);
            Vector2 min = canvasRect.InverseTransformPoint(corners[0]);
            Vector2 max = canvasRect.InverseTransformPoint(corners[2]);
            const float pad = 6f;
            highlight.anchoredPosition = (min + max) * 0.5f;
            highlight.sizeDelta = max - min + new Vector2(pad * 2f, pad * 2f);
            highlight.SetAsLastSibling();

            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f);
            Color color = new Color(1f, 0.75f, 0.3f, pulse);
            foreach(Image bar in highlightBars) bar.color = color;
        }

        // ------------------------------------------------------------------ UI

        private void BuildTutorial(Transform root)
        {
            Image panel = UIFactory.Panel(root, "Tutorial", UIFactory.PanelColor);
            RectTransform rt = panel.rectTransform;
            // Over the conversation, below the stall and money, so the order and the desk stay clear.
            UIFactory.Place(rt, 0f, 0.3f, ConversationWidth, 0.8f, 12f);
            UIFactory.Outline(rt, UIFactory.Accent, 2f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 10f, 18);
            layout.childForceExpandHeight = false;

            tutorialTitle = UIFactory.Label(rt, "TUTORIAL", 16, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(tutorialTitle, 22f, 0f);
            tutorialText = UIFactory.Label(rt, "", 18, TextAnchor.UpperLeft);
            UIFactory.Size(tutorialText, 60f, 1f);

            RectTransform row = UIFactory.Row(rt, 44f, 8f);
            tutorialNext = UIFactory.Button(row, "Next", NextTutorialStep, new Color(0.75f, 0.42f, 0.15f, 1f), 20);
            Button skip = UIFactory.Button(row, "Skip tutorial", () =>
            {
                BeginRealGame();
                Say("Tutorial skipped. Cycle 1: fill the cart, then buy and start the day.");
            }, null, 15);
            UIFactory.Size(skip, -1f, -1f, 150f, 0f);

            tutorialPanel = panel.gameObject;
            tutorialPanel.SetActive(false);

            highlight = UIFactory.Rect(root, "Tutorial Highlight");
            highlight.anchorMin = highlight.anchorMax = new Vector2(0.5f, 0.5f);
            highlightBars = UIFactory.Outline(highlight, UIFactory.Accent, 4f);
            highlight.gameObject.SetActive(false);
        }
    }
}
