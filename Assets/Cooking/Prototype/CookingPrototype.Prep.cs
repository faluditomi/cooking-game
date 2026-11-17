using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Prepping, as on the Miro mock-up. Two cutting boards sit side by side on the workbench; each
    /// takes several units of one kind. The knife, peeler and grater hang from the stall roof: hold one
    /// to pick it up, and do its gesture over a board to prep everything on it. Letting go hangs it back
    /// up. Peeling comes first. Prepped units stay on the board until you drag them somewhere.
    /// </summary>
    public partial class CookingPrototype
    {
        private class CuttingBoard
        {
            public IngredientPortion Units;
            public RectTransform Rect;
            public GesturePadGraphic Pad;
            public RectTransform Pieces;
            public Text Label;
            public RectTransform ProgressFill;
        }

        private readonly List<CuttingBoard> boards = new List<CuttingBoard>();
        private readonly Dictionary<PrepSteps, CanvasGroup> toolHooks = new Dictionary<PrepSteps, CanvasGroup>();
        private PrepSteps heldTool;
        private Gesture prepGesture;
        /// <summary>The board the held tool is over, or -1.</summary>
        private int gestureBoard = -1;

        // ------------------------------------------------------------------ Boards

        private bool PutOnBoard(int index, IngredientPortion portion)
        {
            CuttingBoard board = boards[index];

            if(!portion.IsRaw)
            {
                Say("Cooked food doesn't go back on a board.", UIFactory.Bad);
                return false;
            }

            if(board.Units == null)
            {
                board.Units = new IngredientPortion(portion.ingredient, portion.quantity, portion.prep);
                Say(Describe(board.Units, true) + " on the board. Grab a tool from the stall roof and work over the board.");
                return true;
            }

            if(board.Units.ingredient == portion.ingredient && board.Units.prep == portion.prep)
            {
                board.Units.quantity += portion.quantity;
                Say(Describe(board.Units, true) + " on the board.");
                return true;
            }

            Say("One kind at a time on a board. Use the other board, or move this lot off first.", UIFactory.Bad);
            return false;
        }

        private void GrabFromBoard(int index, Vector2 screen)
        {
            if(!CanPickUp) return;

            CuttingBoard board = boards[index];
            if(board.Units == null) return;

            // The whole board-full at once, into a pot or onto the desk.
            IngredientPortion units = board.Units;
            board.Units = null;
            HoldPortion(units, Origin.Board, index, screen);
        }

        private int BoardAt(Vector2 screen)
        {
            for(int i = 0; i < boards.Count; i++) if(Over(boards[i].Rect, screen)) return i;
            return -1;
        }

        private static Vector2 BoardPoint(CuttingBoard board, Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(board.Rect, screen, null, out Vector2 local);
            return local - board.Rect.rect.center;
        }

        // ------------------------------------------------------------------ Tools

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
                case PrepSteps.Sliced: return "Move it up and down over the board to slice.";
                case PrepSteps.Peeled: return "Draw half circles from the top down over the board to peel.";
                case PrepSteps.Grated: return "Move it left to right over the board to grate. Going back doesn't count.";
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

        /// <summary>Peeling has to come first: once sliced or grated, it can't be peeled.</summary>
        private static bool CanPrep(IngredientPortion unit, PrepSteps step)
        {
            if(unit == null || (unit.prep & step) != 0) return false;
            return step != PrepSteps.Peeled || (unit.prep & (PrepSteps.Sliced | PrepSteps.Grated)) == 0;
        }

        private void GrabTool(PrepSteps step, Vector2 screen)
        {
            if(!CanPickUp) return;

            held = Held.Tool;
            heldFrom = Origin.Hook;
            heldTool = step;
            gestureBoard = -1;
            prepGesture = null;

            Sprite toolSprite = ToolSprite(step);

            if(toolSprite != null)
            {
                CreateGhost(screen, 170f, 170f);
                Image picture = UIFactory.Icon(ghost, toolSprite, 0f);
                UIFactory.Stretch(picture.rectTransform);

                if(step != PrepSteps.Grated)
                {
                    // Knife and peeler hang upright but are held flat: blade to the left, edge down,
                    // with the pointer on the handle.
                    picture.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                    ghost.pivot = new Vector2(0.86f, 0.5f);
                    MoveGhost(screen);
                }
            }
            else
            {
                CreateGhost(screen, 120f, 44f);
                ghost.GetComponent<Image>().color = new Color(0.62f, 0.64f, 0.7f, 0.95f);
                Text label = UIFactory.Label(ghost, ToolName(step).ToUpperInvariant(), 18, TextAnchor.MiddleCenter, FontStyle.Bold, new Color(0.1f, 0.1f, 0.12f, 1f));
                UIFactory.Stretch(label.rectTransform);
            }

            toolHooks[step].alpha = 0.25f;
            foreach(CuttingBoard board in boards) board.Pad.SetGuide(ToolGuide(step), new Color(1f, 0.85f, 0.6f, 0.6f));
            Say("Picked up the " + ToolName(step) + ". " + ToolInstruction(step));
        }

        private void MoveTool(Vector2 screen)
        {
            int index = BoardAt(screen);

            // Once the tool has started on a board it stays locked to it until the prep is done or
            // the tool is put down, so wandering off the board mid-stroke doesn't lose progress.
            if(prepGesture != null && gestureBoard >= 0) index = gestureBoard;

            // Onto another board (or off one, before starting): a fresh go.
            if(index != gestureBoard)
            {
                prepGesture?.End();
                prepGesture = null;
                gestureBoard = index;
                if(index >= 0) StartToolGesture(index, screen);
                RefreshBoards();
                return;
            }

            if(index < 0) return;

            CuttingBoard board = boards[index];
            Vector2 local = BoardPoint(board, screen);
            board.Pad.AddTrailPoint(local);
            if(prepGesture == null || !prepGesture.Move(local)) return;

            UpdateBoardProgress();
            if(!prepGesture.IsComplete) return;

            PrepSteps done = heldTool;
            board.Units.prep |= done;
            prepGesture = null;
            LogCooking("Prep", done + " " + board.Units.quantity + "× " + board.Units.ingredient.DisplayName + " during the order.");
            Say(Describe(board.Units, true) + ". Another tool, or drag it into the pot or pan.", UIFactory.Good);
            RefreshBoards();
        }

        private void StartToolGesture(int index, Vector2 screen)
        {
            CuttingBoard board = boards[index];

            if(board.Units == null) return;

            if((board.Units.prep & heldTool) != 0)
            {
                Say(board.Units.ingredient.DisplayName + " is already " + heldTool.ToString().ToLowerInvariant() + ".");
                return;
            }

            if(!CanPrep(board.Units, heldTool))
            {
                Say("Peel it first: you can't peel something that's already sliced or grated.", UIFactory.Bad);
                return;
            }

            int strokes = RequiredStrokes(heldTool);

            switch(heldTool)
            {
                case PrepSteps.Sliced: prepGesture = new BackAndForthGesture(true, strokes); break;
                case PrepSteps.Peeled: prepGesture = new HalfCircleDownGesture(strokes); break;
                default: prepGesture = new SwipeRightGesture(strokes); break;
            }

            Vector2 local = BoardPoint(board, screen);
            prepGesture.Begin(local);
            board.Pad.AddTrailPoint(local, newStroke: true);
        }

        private void DropTool()
        {
            prepGesture?.End();
            prepGesture = null;
            gestureBoard = -1;
            if(toolHooks.TryGetValue(heldTool, out CanvasGroup hook)) hook.alpha = 1f;
            heldTool = PrepSteps.None;
            foreach(CuttingBoard board in boards) board.Pad.SetGuide(GestureGuide.None, Color.clear);
        }

        private void UpdateBoardProgress()
        {
            for(int i = 0; i < boards.Count; i++)
            {
                float progress = i == gestureBoard && prepGesture != null ? prepGesture.Progress : 0f;
                boards[i].ProgressFill.anchorMax = new Vector2(progress, 1f);
            }
        }

        // ------------------------------------------------------------------ UI

        /// <summary>The prep section: two cutting boards side by side.</summary>
        private void BuildBoards(RectTransform desk)
        {
            RectTransform section = UIFactory.Rect(desk, "Prep Section");
            UIFactory.Place(section, 0.395f, 0.03f, 0.785f, 0.34f);

            for(int i = 0; i < 2; i++)
            {
                int index = i;
                CuttingBoard board = new CuttingBoard();
                Image wood = UIFactory.Panel(section, "Cutting Board", boardSprite != null ? Color.white : new Color(0.56f, 0.41f, 0.27f, 1f));
                wood.sprite = boardSprite;
                wood.preserveAspect = true;
                UIFactory.Place(wood.rectTransform, i * 0.5f, 0f, (i + 1) * 0.5f, 1f, 5f);
                board.Rect = wood.rectTransform;

                RectTransform padRect = UIFactory.Rect(board.Rect, "Pad");
                UIFactory.Stretch(padRect);
                board.Pad = padRect.gameObject.AddComponent<GesturePadGraphic>();
                board.Pad.raycastTarget = true;
                board.Pad.SetTrailColor(new Color(1f, 0.95f, 0.8f, 1f));
                padRect.gameObject.AddComponent<DragHandle>().Began = screen => GrabFromBoard(index, screen);

                board.Pieces = UIFactory.Rect(padRect, "Pieces");
                UIFactory.Place(board.Pieces, 0f, 0.25f, 1f, 0.9f);

                board.Label = UIFactory.Label(padRect, "", 14, TextAnchor.LowerCenter, FontStyle.Bold, new Color(0.15f, 0.1f, 0.06f, 1f));
                UIFactory.Place(board.Label.rectTransform, 0f, 0.02f, 1f, 0.3f, 4f);
                UIFactory.FitText(board.Label);

                Image progressBack = UIFactory.Panel(padRect, "Progress", new Color(0f, 0f, 0f, 0.35f));
                UIFactory.SkinBar(progressBack, new Color(0.25f, 0.18f, 0.12f, 0.8f));
                progressBack.raycastTarget = false;
                UIFactory.Place(progressBack.rectTransform, 0.05f, 0.9f, 0.95f, 0.95f);
                Image fill = UIFactory.Panel(progressBack.transform, "Fill", UIFactory.Good);
                UIFactory.SkinBar(fill, UIFactory.Good);
                fill.raycastTarget = false;
                board.ProgressFill = fill.rectTransform;
                UIFactory.Place(board.ProgressFill, 0f, 0f, 0f, 1f);

                boards.Add(board);
            }
        }

        /// <summary>The knife, peeler and grater hanging from the stall roof.</summary>
        private void BuildToolRail(RectTransform market)
        {
            RectTransform rail = UIFactory.Rect(market, "Prep Tools");
            UIFactory.Place(rail, 0.46f, 0.66f, 0.89f, 1f);

            Image bar = UIFactory.Panel(rail, "Rail", new Color(0.32f, 0.3f, 0.34f, 1f));
            bar.raycastTarget = false;
            UIFactory.Place(bar.rectTransform, 0f, 0.9f, 1f, 0.96f);

            AddTool(rail, PrepSteps.Sliced, "KNIFE", "slice", 0.06f, 0.3f);
            AddTool(rail, PrepSteps.Peeled, "PEELER", "peel", 0.38f, 0.62f);
            AddTool(rail, PrepSteps.Grated, "GRATER", "grate", 0.7f, 0.94f);
        }

        private Sprite ToolSprite(PrepSteps step)
        {
            return step == PrepSteps.Sliced ? knifeSprite : step == PrepSteps.Peeled ? peelerSprite : step == PrepSteps.Grated ? graterSprite : null;
        }

        private void AddTool(RectTransform rail, PrepSteps step, string name, string verb, float xMin, float xMax)
        {
            Sprite sprite = ToolSprite(step);
            float centre = (xMin + xMax) * 0.5f;

            Image hook = UIFactory.Panel(rail, "Hook", new Color(0.5f, 0.5f, 0.55f, 1f));
            hook.raycastTarget = false;
            UIFactory.Place(hook.rectTransform, centre - 0.004f, sprite != null ? 0.84f : 0.62f, centre + 0.004f, 0.92f);

            if(sprite != null)
            {
                // The tool itself hangs from the hook; its name sits small beside it.
                Image picture = UIFactory.Panel(rail, name, Color.white);
                picture.sprite = sprite;
                picture.preserveAspect = true;
                UIFactory.Place(picture.rectTransform, xMin, -0.05f, xMax, 0.86f);
                picture.gameObject.AddComponent<DragHandle>().Began = screen => GrabTool(step, screen);
                toolHooks[step] = picture.gameObject.AddComponent<CanvasGroup>();

                Text caption = UIFactory.Label(rail, name + "\n<size=11><color=#a6a199>hold to " + verb + "</color></size>", 13, TextAnchor.LowerLeft,
                    FontStyle.Bold, UIFactory.TextColor);
                UIFactory.Place(caption.rectTransform, xMax - 0.02f, 0f, xMax + 0.1f, 0.4f);
                return;
            }

            Image tag = UIFactory.Panel(rail, name, new Color(0.62f, 0.64f, 0.7f, 1f));
            UIFactory.Place(tag.rectTransform, xMin, 0.08f, xMax, 0.64f);
            tag.gameObject.AddComponent<DragHandle>().Began = screen => GrabTool(step, screen);
            toolHooks[step] = tag.gameObject.AddComponent<CanvasGroup>();

            Text label = UIFactory.Label(tag.transform, name + "\n<size=13>hold to " + verb + "</size>", 18, TextAnchor.MiddleCenter, FontStyle.Bold,
                new Color(0.1f, 0.1f, 0.12f, 1f));
            UIFactory.Stretch(label.rectTransform, 2f);
        }

        private void RefreshBoards()
        {
            foreach(CuttingBoard board in boards)
            {
                UIFactory.ClearChildren(board.Pieces);

                if(board.Units == null)
                {
                    board.Label.text = "Cutting board";
                    continue;
                }

                AddPile(board.Pieces, board.Units.ingredient.Icon, Mathf.Min(board.Units.quantity, 4), 70f, new Vector2(0.5f, 0.5f));
                board.Label.text = Describe(board.Units, true);
            }

            UpdateBoardProgress();
        }
    }
}
