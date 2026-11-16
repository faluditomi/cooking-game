using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The Prep area: one unit at a time on the board (as in the design doc). Pick up a tool and do
    /// its gesture; finished units go back into stock prepped. Peeling comes first. Open once the day
    /// starts, while the regulars arrive.
    /// </summary>
    public partial class CookingPrototype
    {
        private IngredientPortion boardUnit;
        /// <summary>The stock entry the board unit came from, for "put back and take the next one".</summary>
        private PrepSteps boardSourcePrep;
        private PrepSteps activeTool;
        private Gesture prepGesture;

        private RectTransform prepStockContent;
        private Text boardText;
        private readonly Dictionary<PrepSteps, Button> toolButtons = new Dictionary<PrepSteps, Button>();
        private GesturePadGraphic prepPad;
        private Text prepHint;
        private RectTransform prepProgressFill;
        private Button putBackButton;
        private Button putBackNextButton;

        // ------------------------------------------------------------------ Board

        private void PlaceOnBoard(Inventory.Entry entry)
        {
            if(phase == Phase.DayOver) return;

            // Whatever is on the board goes back to stock first.
            if(boardUnit != null) ReturnBoardToStock();

            if(!inventory.Take(entry.Ingredient, entry.Prep)) return;

            boardUnit = new IngredientPortion(entry.Ingredient, 1, entry.Prep);
            boardSourcePrep = entry.Prep;
            PutDownTool();
            Say(entry.Ingredient.DisplayName + " on the board. Pick up a tool.");
            RefreshAll();
        }

        /// <summary>Puts the board unit back into stock with whatever prep it has now.</summary>
        private void ReturnBoardToStock()
        {
            if(boardUnit == null) return;

            inventory.Add(boardUnit.ingredient, boardUnit.prep);
            boardUnit = null;
            PutDownTool();
        }

        private void PutBack(bool takeNext)
        {
            if(boardUnit == null) return;

            IngredientDefinition ingredient = boardUnit.ingredient;
            string done = Describe(boardUnit, true);
            ReturnBoardToStock();

            if(takeNext && inventory.Count(ingredient, boardSourcePrep) > 0)
            {
                PlaceOnBoard(new Inventory.Entry { Ingredient = ingredient, Prep = boardSourcePrep, Quantity = 1 });
                Say(done + " → stock. Next " + ingredient.DisplayName + " on the board.");
                return;
            }

            Say(done + " → stock." + (takeNext ? " No more like it." : ""));
            RefreshAll();
        }

        // ------------------------------------------------------------------ Tools and gestures

        private static string ToolName(PrepSteps step)
        {
            switch(step)
            {
                case PrepSteps.Sliced: return "knife";
                case PrepSteps.Peeled: return "peeler";
                case PrepSteps.Grated: return "grater";
                default: return "hands";
            }
        }

        private static string ToolInstruction(PrepSteps step)
        {
            switch(step)
            {
                case PrepSteps.Sliced: return "Hold and drag up and down to slice.";
                case PrepSteps.Peeled: return "Hold and drag a half circle from the top down to peel. Bottom to top doesn't count.";
                case PrepSteps.Grated: return "Hold and drag left to right to grate. Going back doesn't count.";
                default: return "";
            }
        }

        private static GestureGuide ToolGuide(PrepSteps step)
        {
            switch(step)
            {
                case PrepSteps.Sliced: return GestureGuide.UpAndDown;
                case PrepSteps.Peeled: return GestureGuide.HalfCircleDown;
                case PrepSteps.Grated: return GestureGuide.LeftToRight;
                default: return GestureGuide.None;
            }
        }

        private int RequiredStrokes(PrepSteps step)
        {
            return step == PrepSteps.Sliced ? sliceStrokes : step == PrepSteps.Peeled ? peelStrokes : grateStrokes;
        }

        private void PickUpTool(PrepSteps step)
        {
            if(boardUnit == null) return;

            if((boardUnit.prep & step) != 0)
            {
                Say(boardUnit.ingredient.DisplayName + " is already " + step.ToString().ToLowerInvariant() + ".");
                return;
            }

            if(!CanPrep(boardUnit, step))
            {
                Say("Peel it first: you can't peel something that's already sliced or grated.", UIFactory.Bad);
                return;
            }

            if(activeTool == step)
            {
                PutDownTool();
                Say("Put the " + ToolName(step) + " down.");
                RefreshAll();
                return;
            }

            activeTool = step;
            int strokes = RequiredStrokes(step);

            switch(step)
            {
                case PrepSteps.Sliced: prepGesture = new BackAndForthGesture(true, strokes); break;
                case PrepSteps.Peeled: prepGesture = new HalfCircleDownGesture(strokes); break;
                default: prepGesture = new SwipeRightGesture(strokes); break;
            }

            Say("Picked up the " + ToolName(step) + ". " + ToolInstruction(step));
            RefreshAll();
        }

        /// <summary>Peeling has to come first: once sliced or grated, it can't be peeled.</summary>
        private static bool CanPrep(IngredientPortion unit, PrepSteps step)
        {
            if(unit == null || (unit.prep & step) != 0) return false;
            return step != PrepSteps.Peeled || (unit.prep & (PrepSteps.Sliced | PrepSteps.Grated)) == 0;
        }

        private void PutDownTool()
        {
            activeTool = PrepSteps.None;
            prepGesture = null;
        }

        private void OnPrepPressed(Vector2 position)
        {
            prepPad.AddTrailPoint(position, newStroke: true);

            if(boardUnit == null)
            {
                Say("Nothing on the board. Pick something from your stock.");
                return;
            }

            if(prepGesture == null)
            {
                Say("Pick up a tool first: knife, peeler or grater.");
                return;
            }

            prepGesture.Begin(position);
        }

        private void OnPrepMoved(Vector2 position)
        {
            prepPad.AddTrailPoint(position);
            if(prepGesture == null || !prepGesture.Move(position)) return;

            UpdatePrepProgress();
            if(!prepGesture.IsComplete) return;

            PrepSteps done = activeTool;
            boardUnit.prep |= done;
            PutDownTool();
            LogCooking("Prep", done + " 1× " + boardUnit.ingredient.DisplayName + " during the order.");

            // The last one of its kind goes back by itself once it's sliced or grated (nothing comes
            // after those; after a peel it stays, in case you slice it next).
            bool last = inventory.Count(boardUnit.ingredient, boardSourcePrep) == 0;
            if(last && done != PrepSteps.Peeled)
            {
                string finished = Describe(boardUnit, true);
                ReturnBoardToStock();
                Say(finished + " → stock. That was the last one.", UIFactory.Good);
                RefreshAll();
                return;
            }

            Say(done + " " + boardUnit.ingredient.DisplayName + ". Another step, or put it back in stock.", UIFactory.Good);
            RefreshAll();
        }

        private void OnPrepReleased()
        {
            prepGesture?.End();
        }

        private void UpdatePrepProgress()
        {
            float progress = prepGesture != null ? prepGesture.Progress : 0f;
            prepProgressFill.anchorMax = new Vector2(progress, 1f);

            if(prepGesture != null)
            {
                prepHint.text = ToolInstruction(activeTool) + "  <color=#a6a199>(" + prepGesture.Strokes + "/" + prepGesture.RequiredStrokes + ")</color>";
            }
        }

        // ------------------------------------------------------------------ UI

        private GameObject BuildPrepArea(RectTransform root)
        {
            RectTransform stock = Column(root, "Stock", 0f, 0.25f);
            VerticalLayoutGroup stockLayout = UIFactory.Vertical(stock.gameObject, 8f, 14);
            stockLayout.childForceExpandHeight = false;
            Header(stock, "STOCK  <color=#77736d>click to put one on the board</color>");
            prepStockContent = UIFactory.ScrollList(stock);

            RectTransform board = Column(root, "Prep Board", 0.25f, 0.72f);
            VerticalLayoutGroup layout = UIFactory.Vertical(board.gameObject, 8f, 14);
            layout.childForceExpandHeight = false;
            Header(board, "PREP BOARD  <color=#77736d>one at a time. Pick up a tool, then hold and drag on the pad</color>");
            boardText = UIFactory.Label(board, "", 24);
            UIFactory.Size(boardText, 32f, 0f);

            RectTransform toolRow = UIFactory.Row(board, 48f);
            toolButtons[PrepSteps.Sliced] = UIFactory.Button(toolRow, "Knife (slice)", () => PickUpTool(PrepSteps.Sliced), null, 20);
            toolButtons[PrepSteps.Peeled] = UIFactory.Button(toolRow, "Peeler (peel)", () => PickUpTool(PrepSteps.Peeled), null, 20);
            toolButtons[PrepSteps.Grated] = UIFactory.Button(toolRow, "Grater (grate)", () => PickUpTool(PrepSteps.Grated), null, 20);

            prepPad = BuildPad(board, "Prep Pad", 300f, 1f, OnPrepPressed, OnPrepMoved, OnPrepReleased);

            Image progressBack = UIFactory.Panel(board, "Prep Progress", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(progressBack, 12f, 0f);
            prepProgressFill = UIFactory.Panel(progressBack.transform, "Fill", UIFactory.Good).rectTransform;
            UIFactory.Place(prepProgressFill, 0f, 0f, 0f, 1f);

            prepHint = UIFactory.Label(board, "", 17);
            UIFactory.Size(prepHint, 26f, 0f);

            RectTransform putRow = UIFactory.Row(board, 48f);
            putBackButton = UIFactory.Button(putRow, "Put back in stock", () => PutBack(false), null, 19);
            putBackNextButton = UIFactory.Button(putRow, "Put back + next one", () => PutBack(true), new Color(0.25f, 0.4f, 0.25f, 1f), 19);

            BuildTicket(Column(root, "Ticket", 0.72f, 1f));
            return root.gameObject;
        }

        private void RefreshPrep()
        {
            if(prepStockContent == null) return;

            if(area == Area.Prep) RebuildStockList(prepStockContent, PlaceOnBoard, phase != Phase.DayOver);

            boardText.text = boardUnit == null
                ? "<color=#a6a199>Empty. Click something in your stock.</color>"
                : Describe(boardUnit, true);

            foreach(KeyValuePair<PrepSteps, Button> pair in toolButtons)
            {
                pair.Value.interactable = CanPrep(boardUnit, pair.Key);
                pair.Value.GetComponent<Image>().color = activeTool == pair.Key ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor;
            }

            putBackButton.interactable = boardUnit != null;
            putBackNextButton.interactable = boardUnit != null;

            prepPad.SetDisc(boardUnit != null, boardUnit != null ? IngredientColor(boardUnit.ingredient) : Color.clear);
            prepPad.SetGuide(ToolGuide(activeTool), new Color(1f, 0.85f, 0.6f, 0.6f));

            if(boardUnit == null) prepHint.text = "<color=#a6a199>Put something on the board, then pick up a tool.</color>";
            else if(prepGesture == null) prepHint.text = "<color=#a6a199>Pick up a tool, or put it back in stock.</color>";

            UpdatePrepProgress();
        }

        private static Color IngredientColor(IngredientDefinition ingredient)
        {
            // Stable, readable colour per ingredient until there are sprites.
            float hue = (ingredient.DisplayName.GetHashCode() & 0xFFFF) / 65535f;
            Color color = Color.HSVToRGB(hue, 0.45f, 0.75f);
            color.a = 0.9f;
            return color;
        }
    }
}
