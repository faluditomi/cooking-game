using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The workbench (service only), as on the Miro mock-up. Everything is press-and-hold:
    /// <list type="bullet">
    /// <item><b>Pantry</b>: ingredients piled by kind. Hold one to take a single unit; drop it on a
    /// cutting board, the pot, the pan, a plate (seasonings only, on a cooked dish) or free desk space.
    /// Anywhere else it goes back on its pile.</item>
    /// <item><b>Clean plates</b>: an endless stack. A plate dropped on the desk goes to the nearest free
    /// space. Empty plates go back on the stack; a plate with anything on it stays on the desk.</item>
    /// <item><b>Pot and pan</b>: drop ingredients in, then click to light. A dish is one batch. Holding a
    /// lit pot or pan takes it off the heat (its cycle pauses, the bar stays with it); drop it on a
    /// plate to dish up, anywhere else to put it back. The pan is stirred by shaking it while held.</item>
    /// <item><b>Food delivery</b>: dropping a plate here serves it.</item>
    /// </list>
    /// </summary>
    public partial class CookingPrototype
    {
        private const int DeskSlotColumns = 3;
        private const int DeskSlotRows = 2;

        private class Plate
        {
            public readonly List<IngredientPortion> Items = new List<IngredientPortion>();
            /// <summary>Ingredient cost of what's on it.</summary>
            public float Cost;

            public bool HasDish
            {
                get
                {
                    foreach(IngredientPortion portion in Items) if(!portion.IsRaw) return true;
                    return false;
                }
            }
        }

        /// <summary>A free spot on the desk: holds one plate, or loose units of one kind.</summary>
        private class DeskSlot
        {
            public RectTransform Rect;
            public RectTransform Content;
            public Plate Plate;
            public IngredientPortion Loose;
            public bool IsEmpty => Plate == null && Loose == null;
        }

        private class StationView
        {
            public CookingStation Station;
            public RectTransform Rect;
            /// <summary>The pot or pan picture with what's in it; hidden while it's lifted off.</summary>
            public Cookware Pot;
            public Text Contents;
            public Text Status;
            public TimingBar Bar;
            public Button EmptyButton;
            /// <summary>Ingredient cost of what's in it, passed on to the plate.</summary>
            public float Cost;
        }

        /// <summary>A drawn pot or pan: the picture, the pieces inside, and water for a soup.</summary>
        private class Cookware
        {
            public GameObject Root;
            public RectTransform Inside;
            public Image Water;
        }

        private enum Held
        {
            None,
            Portion,
            Plate,
            Station,
            Tool,
        }

        private enum Origin
        {
            Pantry,
            Board,
            Slot,
            Stack,
            Stove,
            Hook,
        }

        private CookingStation wok;
        private CookingStation pot;
        private Gesture shakeGesture;
        /// <summary>The plate being served, for judging and the result screen.</summary>
        private readonly List<IngredientPortion> plate = new List<IngredientPortion>();
        private readonly List<DeskSlot> deskSlots = new List<DeskSlot>();
        /// <summary>The plate the order page's radar shows: the last one something went onto.</summary>
        private Plate focusPlate;

        private StationView wokView;
        private StationView potView;
        private RectTransform pantryRect;
        private RectTransform pantryContent;
        private RectTransform plateStack;
        private RectTransform deliveryRect;
        private Image[] deliveryOutline;
        private Text deliveryLabel;
        private Button previewButton;
        private FlavourRadarGraphic plateRadar;

        // What's in hand
        private Held held;
        private Origin heldFrom;
        private int heldIndex;
        private IngredientPortion heldPortion;
        private Plate heldPlate;
        private StationView heldStation;
        /// <summary>The pan was stirred during this lift: more shaking doesn't count until it's back on the heat.</summary>
        private bool stirredThisLift;
        private RectTransform ghost;
        private TimingBar ghostBar;
        private Text ghostStatus;

        private void CreateKitchenState()
        {
            wok = CookingStation.CreateWok(wokSeconds);
            pot = CookingStation.CreatePot(potSeconds);
            shakeGesture = new BackAndForthGesture(vertical: false, requiredStrokes: int.MaxValue, minTravel: 45f);
        }

        private void TickKitchen(float deltaTime)
        {
            // Held off the heat, a pot or pan pauses and carries on where it left off when it's put back.
            if(heldStation != wokView) wok.Tick(deltaTime);
            if(heldStation != potView) pot.Tick(deltaTime);
            UpdateStationView(wokView);
            UpdateStationView(potView);
        }

        /// <summary>Empties the stations and the desk (end of the cycle). Put stock back first with <see cref="ReturnDeskToStock"/>.</summary>
        private void ClearKitchen()
        {
            DropWithoutEffect();
            wok.Clear();
            pot.Clear();
            if(wokView != null) wokView.Cost = 0f;
            if(potView != null) potView.Cost = 0f;
            foreach(DeskSlot slot in deskSlots)
            {
                slot.Plate = null;
                slot.Loose = null;
            }

            foreach(CuttingBoard board in boards) board.Units = null;
            focusPlate = null;
            ClearPlate();
        }

        private void ClearPlate()
        {
            plate.Clear();
        }

        /// <summary>Loose units on the desk and on the boards go back into stock, as prepped as they got.</summary>
        private void ReturnDeskToStock()
        {
            DropWithoutEffect();

            foreach(DeskSlot slot in deskSlots)
            {
                if(slot.Loose == null) continue;
                inventory.Add(slot.Loose.ingredient, slot.Loose.prep, slot.Loose.quantity);
                slot.Loose = null;
            }

            foreach(CuttingBoard board in boards)
            {
                if(board.Units == null) continue;
                inventory.Add(board.Units.ingredient, board.Units.prep, board.Units.quantity);
                board.Units = null;
            }
        }

        /// <summary>Raw or prepped units out on the desk or the boards (not in stock).</summary>
        private bool DeskHasIngredients()
        {
            foreach(DeskSlot slot in deskSlots) if(slot.Loose != null) return true;
            foreach(CuttingBoard board in boards) if(board.Units != null) return true;
            return false;
        }

        /// <summary>Units of <paramref name="ingredient"/> on the desk and boards, all of them or just those prepped as <paramref name="prep"/>.</summary>
        private int OnDesk(IngredientDefinition ingredient, PrepSteps? prep = null)
        {
            int count = 0;

            foreach(DeskSlot slot in deskSlots)
            {
                if(slot.Loose != null && slot.Loose.ingredient == ingredient && (prep == null || slot.Loose.prep == prep)) count += slot.Loose.quantity;
            }

            foreach(CuttingBoard board in boards)
            {
                if(board.Units != null && board.Units.ingredient == ingredient && (prep == null || board.Units.prep == prep)) count += board.Units.quantity;
            }

            return count;
        }

        private bool DeskOpen()
        {
            if(phase == Phase.Service && !overlayOpen) return true;
            Say(phase == Phase.Morning ? "The stall opens when you start the day. Buy what you need on the tablet first." : "The stall is closed.", UIFactory.Bad);
            return false;
        }

        // ------------------------------------------------------------------ Picking things up

        private bool CanPickUp => held == Held.None && DeskOpen();

        private void GrabFromPantry(Inventory.Entry entry, Vector2 screen)
        {
            if(!CanPickUp) return;
            // Always just one, however many are in the pile.
            if(!inventory.Take(entry.Ingredient, entry.Prep)) return;
            HoldPortion(new IngredientPortion(entry.Ingredient, 1, entry.Prep), Origin.Pantry, 0, screen);
        }

        private void GrabFromSlot(DeskSlot slot, Vector2 screen)
        {
            if(!CanPickUp) return;

            int index = deskSlots.IndexOf(slot);

            if(slot.Plate != null)
            {
                Plate taken = slot.Plate;
                slot.Plate = null;
                HoldPlate(taken, Origin.Slot, index, screen);
            }
            else if(slot.Loose != null)
            {
                IngredientPortion taken = slot.Loose;
                slot.Loose = null;
                HoldPortion(taken, Origin.Slot, index, screen);
            }
        }

        private void GrabCleanPlate(Vector2 screen)
        {
            if(!CanPickUp) return;
            HoldPlate(new Plate(), Origin.Stack, 0, screen);
        }

        private void GrabStation(StationView view, Vector2 screen)
        {
            if(!CanPickUp) return;

            CookingStation station = view.Station;

            if(!station.IsCooking)
            {
                Say(station.IsEmpty ? "The " + station.Name.ToLowerInvariant() + " is empty. Drop ingredients in from the pantry or a board."
                    : "Click the " + station.Name.ToLowerInvariant() + " to light it first.");
                return;
            }

            held = Held.Station;
            heldFrom = Origin.Stove;
            heldStation = view;
            stirredThisLift = false;

            // Just the pot or pan; its bar stays on the station.
            CreateGhost(screen, 240f, 250f);
            Cookware lifted = BuildCookware(ghost, station);
            FillCookware(lifted, station);

            if(station.Stirs) shakeGesture.Begin(CanvasPoint(screen));
            Say(station.Stirs ? "Pan off the heat. Shake it side to side in the green to stir one unit. Drop it on a plate to dish up, anywhere else to put it back."
                : "Pot off the heat. Drop it on a plate to dish up, anywhere else to put it back.");
            RefreshAll();
        }

        private void HoldPortion(IngredientPortion portion, Origin from, int index, Vector2 screen)
        {
            held = Held.Portion;
            heldFrom = from;
            heldIndex = index;
            heldPortion = portion;

            CreateGhost(screen, 150f, 90f);
            AddPile(ghost, portion.ingredient.Icon, Mathf.Min(portion.quantity, 3), 52f, new Vector2(0.5f, 0.62f));
            Text label = UIFactory.Label(ghost, Describe(portion, true), 14, TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(label.rectTransform, -0.3f, -0.1f, 1.3f, 0.3f);
            label.raycastTarget = false;
            RefreshAll();
        }

        private void HoldPlate(Plate taken, Origin from, int index, Vector2 screen)
        {
            held = Held.Plate;
            heldFrom = from;
            heldIndex = index;
            heldPlate = taken;
            if(taken.Items.Count > 0) focusPlate = taken;

            CreateGhost(screen, 110f, 110f);
            BuildPlateVisual(ghost, taken);
            SetDeliveryHighlight(true);
            RefreshAll();
        }

        // ------------------------------------------------------------------ Holding and dropping

        /// <summary>Follows the pointer while something is held; drops it when the button comes up.</summary>
        private void UpdateHeld()
        {
            if(held == Held.None) return;

            Pointer pointer = Pointer.current;
            Vector2 position = pointer != null ? pointer.position.ReadValue() : Vector2.zero;

            if(pointer == null || !pointer.press.isPressed)
            {
                DropHeld(position);
                return;
            }

            MoveGhost(position);

            if(held == Held.Station)
            {
                if(heldStation.Station.Stirs) ShakeHeld(position);
                UpdateGhostStation();
            }
            else if(held == Held.Tool)
            {
                MoveTool(position);
            }
        }

        private void DropHeld(Vector2 screen)
        {
            Held what = held;
            held = Held.None;
            DestroyGhost();
            SetDeliveryHighlight(false);

            switch(what)
            {
                case Held.Portion: DropPortion(screen); break;
                case Held.Plate: DropPlate(screen); break;
                case Held.Station: DropStation(screen); break;
                case Held.Tool: DropTool(); break;
            }

            RefreshAll();
        }

        /// <summary>Puts whatever is held back where it came from (the day ended, or a reset).</summary>
        private void DropWithoutEffect()
        {
            if(held == Held.None) return;

            Held what = held;
            held = Held.None;
            DestroyGhost();
            SetDeliveryHighlight(false);

            if(what == Held.Portion) ReturnPortion(heldPortion);
            else if(what == Held.Plate) ReturnPlate(heldPlate);
            else if(what == Held.Tool) DropTool();

            heldPortion = null;
            heldPlate = null;
            heldStation = null;
        }

        private void DropPortion(Vector2 screen)
        {
            IngredientPortion portion = heldPortion;
            heldPortion = null;
            string name = Describe(portion, true);

            int boardIndex = BoardAt(screen);

            if(boardIndex >= 0)
            {
                if(!PutOnBoard(boardIndex, portion)) ReturnPortion(portion);
                return;
            }

            StationView station = StationAt(screen);

            if(station != null)
            {
                if(!station.Station.CanAdd(portion, out string reason))
                {
                    Say(reason, UIFactory.Bad);
                    ReturnPortion(portion);
                    return;
                }

                station.Station.Add(portion);
                station.Cost += portion.ingredient.Price * portion.quantity;
                LogCooking(station.Station.Name, "Put in " + ListPortions(new[] { portion }) + (station.Station.IsSoup ? " (soup)." : "."));
                Say(name + " into the " + station.Station.Name.ToLowerInvariant() + (station.Station.IsSoup ? " (soup)" : "")
                    + ". Click it to light it once everything's in.");
                return;
            }

            DeskSlot slot = SlotAt(screen);

            if(slot != null)
            {
                if(slot.Plate != null)
                {
                    if(!Season(slot.Plate, portion)) ReturnPortion(portion);
                    return;
                }

                if(slot.Loose == null)
                {
                    slot.Loose = portion;
                    Say(name + " on the desk.");
                    return;
                }

                if(SameKind(slot.Loose, portion))
                {
                    slot.Loose.quantity += portion.quantity;
                    return;
                }

                Say("Something else is there already.", UIFactory.Bad);
                ReturnPortion(portion);
                return;
            }

            if(Over(deliveryRect, screen)) Say("Only a plate goes here: put it on a plate first.", UIFactory.Bad);

            // Over the pantry, or anywhere else: back on its pile.
            if(Over(pantryRect, screen)) inventory.Add(portion.ingredient, portion.prep, portion.quantity);
            else ReturnPortion(portion);
        }

        /// <summary>Back where it was picked up from.</summary>
        private void ReturnPortion(IngredientPortion portion)
        {
            if(portion == null) return;

            if(heldFrom == Origin.Board && heldIndex >= 0 && heldIndex < boards.Count && boards[heldIndex].Units == null)
            {
                boards[heldIndex].Units = portion;
                return;
            }

            if(heldFrom == Origin.Slot && heldIndex >= 0 && heldIndex < deskSlots.Count && deskSlots[heldIndex].IsEmpty)
            {
                deskSlots[heldIndex].Loose = portion;
                return;
            }

            inventory.Add(portion.ingredient, portion.prep, portion.quantity);
        }

        private static bool SameKind(IngredientPortion a, IngredientPortion b)
        {
            return a.ingredient == b.ingredient && a.prep == b.prep && a.cooking == b.cooking && a.IsRaw == b.IsRaw && (a.IsRaw || a.doneness == b.doneness);
        }

        /// <summary>
        /// Something raw onto a plate. A cooked dish only takes seasonings. A plate without one takes
        /// anything raw, served as it is (some regulars ask for a plain potato), but then no cooked
        /// dish goes on it.
        /// </summary>
        private bool Season(Plate target, IngredientPortion portion)
        {
            if(target.HasDish && !portion.ingredient.IsSeasoning)
            {
                Say(portion.ingredient.DisplayName + " can't go on a cooked dish. Everything for a dish is cooked in the pot or the pan; only seasonings go on after.", UIFactory.Bad);
                return false;
            }

            AddToPlate(target, portion);
            target.Cost += portion.ingredient.Price * portion.quantity;
            focusPlate = target;

            if(target.HasDish)
            {
                Say("Seasoned the dish with " + ListPortions(new[] { portion }) + ".");
                LogCooking("Plate", "Seasoned with " + ListPortions(new[] { portion }) + ".");
            }
            else
            {
                Say(ListPortions(new[] { portion }) + " on the plate, raw. Serve it as it is; a cooked dish needs a clean plate.");
                LogCooking("Plate", "Put " + ListPortions(new[] { portion }) + " on the plate, raw.");
            }

            return true;
        }

        /// <summary>Raw food other than seasonings on the plate: it's a raw plate, not one for a cooked dish.</summary>
        private static bool HasRawFood(Plate target)
        {
            foreach(IngredientPortion portion in target.Items) if(portion.IsRaw && !portion.ingredient.IsSeasoning) return true;
            return false;
        }

        private void DropPlate(Vector2 screen)
        {
            Plate dropped = heldPlate;
            heldPlate = null;

            if(Over(plateStack, screen))
            {
                if(dropped.Items.Count == 0) return;
                Say("A plate with food on it stays on the desk. Serve it.", UIFactory.Bad);
                ReturnPlate(dropped);
                return;
            }

            if(Over(deliveryRect, screen))
            {
                if(!TryServe(dropped)) ReturnPlate(dropped);
                return;
            }

            DeskSlot slot = SlotAt(screen);

            if(slot != null && slot.IsEmpty)
            {
                slot.Plate = dropped;
                return;
            }

            // Anywhere on the desk: the nearest free space.
            if(Over(deskRect, screen))
            {
                DeskSlot nearest = NearestFreeSlot(screen);

                if(nearest != null)
                {
                    nearest.Plate = dropped;
                    return;
                }

                Say("No room on the desk.", UIFactory.Bad);
            }

            ReturnPlate(dropped);
        }

        private void ReturnPlate(Plate dropped)
        {
            if(dropped == null || heldFrom == Origin.Stack) return;

            DeskSlot slot = heldIndex >= 0 && heldIndex < deskSlots.Count && deskSlots[heldIndex].IsEmpty ? deskSlots[heldIndex] : NearestFreeSlot(null);
            if(slot != null) slot.Plate = dropped;
        }

        private void DropStation(Vector2 screen)
        {
            StationView view = heldStation;
            heldStation = null;
            shakeGesture.End();

            DeskSlot slot = SlotAt(screen);

            if(slot != null && slot.Plate != null)
            {
                DishUp(view, slot.Plate);
                return;
            }

            if(slot != null || Over(deliveryRect, screen))
            {
                Say("Put a clean plate down first, from the stack. The " + view.Station.Name.ToLowerInvariant() + " is back on the heat.", UIFactory.Bad);
                return;
            }

            Say("Back on the heat.");
        }

        /// <summary>Everything in the station onto <paramref name="target"/>, done as it is now.</summary>
        private void DishUp(StationView view, Plate target)
        {
            // One dish per plate: no more cooked food once there's a dish on it.
            if(target.HasDish || HasRawFood(target))
            {
                Say((target.HasDish ? "There's already a dish on that plate." : "There's raw food on that plate.") + " Use a clean one. The "
                    + view.Station.Name.ToLowerInvariant() + " is back on the heat.", UIFactory.Bad);
                return;
            }

            CookingStation station = view.Station;
            Doneness doneness = station.CurrentDoneness;
            float progress = station.Progress;
            string wokSummary = station.Stirs ? WokSummary(station) : null;
            bool allPerfect = station.Stirs ? station.StirredCount - station.OverdoneCount == station.UnitCount : doneness == Doneness.Perfect;
            List<IngredientPortion> cooked = station.TakeOff();
            if(cooked.Count == 0) return;

            foreach(IngredientPortion portion in cooked) AddToPlate(target, portion);
            target.Cost += view.Cost;
            view.Cost = 0f;
            focusPlate = target;

            if(wokSummary != null)
            {
                LogCooking(station.Name, "Took it off: " + wokSummary + ".");
                Say(station.Name + " onto the plate: " + wokSummary + ". Drag the plate to the food delivery spot to serve.", allPerfect ? UIFactory.Good : UIFactory.Bad);
            }
            else
            {
                LogCooking(station.Name, "Took it off at " + (progress * 100f).ToString("0") + "%: " + DonenessLabel(doneness) + ".");
                Say(station.Name + " onto the plate: " + DonenessLabel(doneness) + ". Drag the plate to the food delivery spot to serve.", allPerfect ? UIFactory.Good : UIFactory.Bad);
            }
        }

        private void ShakeHeld(Vector2 screen)
        {
            if(!shakeGesture.Move(CanvasPoint(screen))) return;

            // One stir per lift: put it back on the heat to get to the green again.
            if(stirredThisLift)
            {
                Say("Stirred. Put it back on the heat until the bar is green again.");
                return;
            }

            switch(wok.Shake())
            {
                case ShakeResult.Stirred:
                    stirredThisLift = true;
                    Say("Stirred one in the green. " + WokSummary(wok) + ". Back on the heat for the next.", UIFactory.Good);
                    break;
                case ShakeResult.StirredOverdone:
                    stirredThisLift = true;
                    Say("Stirred one, but in the red: a bit burned. " + WokSummary(wok) + ".", UIFactory.Accent);
                    break;
                case ShakeResult.TooEarly:
                    Say("Too early: the bar went back to the start. Shake in the green.", UIFactory.Bad);
                    break;
                case ShakeResult.KnockedBack:
                    stirredThisLift = true;
                    Say(wok.UnstirredCount == 0 ? "Everything's stirred. Drop it on a plate." : "Knocked back. Shake in the green to stir.");
                    break;
            }

            RefreshStationContents(wokView);
        }

        private void AddToPlate(Plate target, IngredientPortion portion)
        {
            foreach(IngredientPortion existing in target.Items)
            {
                if(existing.ingredient == portion.ingredient && existing.prep == portion.prep
                   && existing.cooking == portion.cooking && (existing.IsRaw || existing.doneness == portion.doneness))
                {
                    existing.quantity += portion.quantity;
                    return;
                }
            }

            target.Items.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, portion.doneness));
        }

        private bool TryServe(Plate dropped)
        {
            if(customer == null || !orderIn)
            {
                Say(customer == null ? "Nobody's at the counter." : "Wait for their order.", UIFactory.Bad);
                return false;
            }

            if(dropped.Items.Count == 0)
            {
                Say("The plate is empty.", UIFactory.Bad);
                return false;
            }

            plate.Clear();
            plate.AddRange(dropped.Items);
            dishCost = dropped.Cost;
            if(focusPlate == dropped) focusPlate = null;

            JudgeResult result = DishJudge.Judge(order, plate, customer, zones, rules, JudgeSettings());
            if(customer.alwaysPaysAsStranger && Relationship(customer).Stage == NpcStage.Stranger) result = DishJudge.PayInFull(result);
            FinishOrder(result);
            return true;
        }

        private void StartStation(StationView view)
        {
            if(phase != Phase.Service || held != Held.None) return;

            if(view.Station.IsEmpty)
            {
                Say("The " + view.Station.Name.ToLowerInvariant() + " is empty. Drop ingredients in from the pantry or a board first.");
                return;
            }

            if(view.Station.IsCooking)
            {
                Say(view.Station.Stirs ? "Hold the pan to take it off the heat, and shake it to stir." : "Hold the pot and drop it on a plate when the bar is green.");
                return;
            }

            if(view.Station.StartCooking())
            {
                Say(view.Station.Name + " on. Watch the bar.");
                LogCooking(view.Station.Name, "Started" + (view.Station.IsSoup ? " the soup." : "."));
            }

            RefreshAll();
        }

        /// <summary>Takes everything back out of an unlit station, into stock.</summary>
        private void EmptyStation(StationView view)
        {
            if(view.Station.IsCooking || view.Station.IsEmpty) return;

            foreach(IngredientPortion portion in view.Station.Contents) inventory.Add(portion.ingredient, portion.prep, portion.quantity);
            LogCooking(view.Station.Name, "Took " + ListPortions(view.Station.Contents) + " back out.");
            view.Station.Clear();
            view.Cost = 0f;
            Say("Took everything back out of the " + view.Station.Name.ToLowerInvariant() + ".");
            RefreshAll();
        }

        /// <summary>"3/4 stirred (1 overcooked), 1 burnt, 0 not stirred".</summary>
        private static string WokSummary(CookingStation station)
        {
            string text = station.StirredCount + "/" + station.UnitCount + " stirred";
            if(station.OverdoneCount > 0) text += " (" + station.OverdoneCount + " overcooked)";
            if(station.BurntCount > 0) text += ", " + station.BurntCount + " burnt";
            if(station.UnstirredCount > 0) text += ", " + station.UnstirredCount + " not stirred";
            return text;
        }

        private void TogglePreview()
        {
            livePreview = !livePreview;
            RefreshAll();
        }

        private DishJudge.Settings JudgeSettings()
        {
            DishJudge.Settings settings = DishJudge.Settings.Default;
            settings.Margin = margin;
            settings.Similarity = new DishSimilarity.Settings { FlavourWeight = flavourWeight, FlavourRange = flavourRange };
            settings.VeryCloseAt = veryCloseAt;
            settings.VeryClosePay = veryClosePay;
            settings.CloseEnoughAt = closeEnoughAt;
            settings.CloseEnoughPay = closeEnoughPay;
            return settings;
        }

        /// <summary>"2× Rice, 1× Onion (peeled, sliced)" for the history.</summary>
        private static string ListPortions(IReadOnlyList<IngredientPortion> portions)
        {
            List<string> items = new List<string>();

            foreach(IngredientPortion portion in portions)
            {
                if(portion == null || !portion.IsValid) continue;
                string item = portion.quantity + "× " + portion.ingredient.DisplayName;
                List<string> prep = new List<string>();
                if((portion.prep & PrepSteps.Peeled) != 0) prep.Add("peeled");
                if((portion.prep & PrepSteps.Sliced) != 0) prep.Add("sliced");
                if((portion.prep & PrepSteps.Grated) != 0) prep.Add("grated");
                if(prep.Count > 0) item += " (" + string.Join(", ", prep) + ")";
                items.Add(item);
            }

            return items.Count > 0 ? string.Join(", ", items) : "nothing";
        }

        // ------------------------------------------------------------------ Hit tests

        private static bool Over(RectTransform rect, Vector2 screen)
        {
            return rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);
        }

        private DeskSlot SlotAt(Vector2 screen)
        {
            foreach(DeskSlot slot in deskSlots) if(Over(slot.Rect, screen)) return slot;
            return null;
        }

        private StationView StationAt(Vector2 screen)
        {
            if(Over(wokView.Rect, screen)) return wokView;
            if(Over(potView.Rect, screen)) return potView;
            return null;
        }

        /// <summary>The free desk space nearest the pointer (or the first free one).</summary>
        private DeskSlot NearestFreeSlot(Vector2? screen)
        {
            DeskSlot best = null;
            float bestDistance = float.MaxValue;

            foreach(DeskSlot slot in deskSlots)
            {
                if(!slot.IsEmpty) continue;
                if(screen == null) return slot;

                Vector2 centre = RectTransformUtility.WorldToScreenPoint(null, slot.Rect.TransformPoint(slot.Rect.rect.center));
                float distance = (centre - screen.Value).sqrMagnitude;

                if(distance < bestDistance)
                {
                    bestDistance = distance;
                    best = slot;
                }
            }

            return best;
        }

        private Vector2 CanvasPoint(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            return local;
        }

        // ------------------------------------------------------------------ The thing in hand

        private void CreateGhost(Vector2 screen, float width, float height)
        {
            DestroyGhost();
            Image image = UIFactory.Panel(canvasRect, "Held", Color.clear);
            image.raycastTarget = false;
            ghost = image.rectTransform;
            ghost.sizeDelta = new Vector2(width, height);
            ghost.SetAsLastSibling();
            MoveGhost(screen);
        }

        private void MoveGhost(Vector2 screen)
        {
            if(ghost != null) ghost.anchoredPosition = CanvasPoint(screen);
        }

        private void DestroyGhost()
        {
            if(ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            ghostBar = null;
            ghostStatus = null;
        }

        private void UpdateGhostStation()
        {
            if(ghostBar == null || heldStation == null) return;

            CookingStation station = heldStation.Station;
            ghostBar.Set(station.Progress, true);
            ghostStatus.text = station.Stirs ? WokSummary(station) : (station.Progress * 100f).ToString("0") + "%  " + DonenessLabel(station.CurrentDoneness);
        }

        private void SetDeliveryHighlight(bool on)
        {
            if(deliveryOutline == null) return;
            foreach(Image bar in deliveryOutline) bar.color = on ? UIFactory.Accent : new Color(1f, 1f, 1f, 0.5f);
        }

        // ------------------------------------------------------------------ UI

        /// <summary>
        /// The pot or pan, drawn to fit <paramref name="area"/>: its picture, a dark inside (the pot),
        /// the pieces in it, and water over them once a soup is cooking.
        /// </summary>
        private Cookware BuildCookware(RectTransform area, CookingStation station)
        {
            bool isPan = station == wok;
            Sprite sprite = isPan ? panSprite : potSprite;
            Cookware cookware = new Cookware();

            // Keeps the picture's shape, so the inside lines up with the drawing.
            RectTransform root = UIFactory.Rect(area, isPan ? "Pan" : "Pot");
            UIFactory.Stretch(root);
            AspectRatioFitter fitter = root.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = sprite != null ? sprite.rect.width / sprite.rect.height : 1f;
            cookware.Root = root.gameObject;

            Image picture = UIFactory.Panel(root, "Picture", sprite != null ? Color.white : new Color(0.3f, 0.31f, 0.35f, 1f));
            picture.sprite = sprite != null ? sprite : UIFactory.Circle;
            picture.raycastTarget = false;
            UIFactory.Stretch(picture.rectTransform);

            // Where the bowl is in each drawing (the handle sticks out at the bottom left).
            Vector4 inside = sprite == null ? new Vector4(0.15f, 0.15f, 0.85f, 0.85f)
                : isPan ? new Vector4(0.14f, 0.36f, 0.86f, 0.9f)
                : new Vector4(0.1f, 0.36f, 0.9f, 0.94f);

            if(!isPan)
            {
                // The renders are flat-lit, so the pot's opening is drawn in.
                Image hollow = UIFactory.Panel(root, "Inside", new Color(0.2f, 0.21f, 0.25f, 1f));
                hollow.sprite = UIFactory.Circle;
                hollow.raycastTarget = false;
                UIFactory.Place(hollow.rectTransform, inside.x, inside.y, inside.z, inside.w);
            }

            cookware.Inside = UIFactory.Rect(root, "Pieces");
            UIFactory.Place(cookware.Inside, inside.x + 0.06f, inside.y + 0.06f, inside.z - 0.06f, inside.w - 0.06f);

            cookware.Water = UIFactory.Panel(root, "Water", new Color(0.45f, 0.62f, 0.78f, 0.5f));
            cookware.Water.sprite = UIFactory.Circle;
            cookware.Water.raycastTarget = false;
            UIFactory.Place(cookware.Water.rectTransform, inside.x + 0.02f, inside.y + 0.02f, inside.z - 0.02f, inside.w - 0.02f);
            cookware.Water.gameObject.SetActive(false);
            return cookware;
        }

        /// <summary>The station's ingredients, heaped and overlapping inside the pot or pan.</summary>
        private static void FillCookware(Cookware cookware, CookingStation station)
        {
            UIFactory.ClearChildren(cookware.Inside);

            List<Sprite> pieces = new List<Sprite>();
            foreach(IngredientPortion portion in station.Contents)
            {
                for(int i = 0; i < portion.quantity && pieces.Count < 12; i++) pieces.Add(portion.ingredient.Icon);
            }

            // A sunflower spiral spreads them out evenly; each is tipped a little, so it looks piled in.
            for(int i = 0; i < pieces.Count; i++)
            {
                float radius = pieces.Count == 1 ? 0f : 0.36f * Mathf.Sqrt((i + 0.5f) / pieces.Count);
                float angle = i * 2.39996f;
                Vector2 centre = new Vector2(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius * 0.8f);
                float size = pieces.Count <= 3 ? 0.5f : pieces.Count <= 6 ? 0.4f : 0.32f;

                Image piece = UIFactory.Panel(cookware.Inside, "Piece", pieces[i] != null ? Color.white : Color.clear);
                piece.sprite = pieces[i];
                piece.preserveAspect = true;
                piece.raycastTarget = false;
                UIFactory.Place(piece.rectTransform, centre.x - size * 0.5f, centre.y - size * 0.5f, centre.x + size * 0.5f, centre.y + size * 0.5f);
                piece.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ((i * 47) % 50) - 25f);
            }

            cookware.Water.gameObject.SetActive(station.IsSoup && station.IsCooking);
            cookware.Water.transform.SetAsLastSibling();
        }

        private void BuildDesk(Transform root)
        {
            Image desk = UIFactory.Panel(root, "Workbench", new Color(0.23f, 0.17f, 0.13f, 1f));
            deskRect = desk.rectTransform;
            UIFactory.Place(deskRect, ConversationWidth, 0f, 1f, DeskShare);

            Image edge = UIFactory.Panel(deskRect, "Edge", new Color(0.36f, 0.27f, 0.2f, 1f));
            edge.raycastTarget = false;
            RectTransform edgeRect = edge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = Vector2.one;
            edgeRect.offsetMin = new Vector2(0f, -8f);
            edgeRect.offsetMax = Vector2.zero;

            BuildDelivery(deskRect);

            // Free desk space for plates and loose ingredients.
            for(int row = 0; row < DeskSlotRows; row++)
            {
                for(int column = 0; column < DeskSlotColumns; column++)
                {
                    float x0 = 0.14f + column * (0.245f / DeskSlotColumns);
                    float y1 = 0.97f - row * (0.35f / DeskSlotRows);
                    deskSlots.Add(BuildDeskSlot(deskRect, x0, y1 - 0.35f / DeskSlotRows, x0 + 0.245f / DeskSlotColumns, y1));
                }
            }

            potView = BuildStationTile(deskRect, pot, "COOKING (POT)", 0.395f, 0.585f);
            wokView = BuildStationTile(deskRect, wok, "FRYING (PAN)", 0.595f, 0.785f);
            BuildPlateStack(deskRect);
            BuildBoards(deskRect);
            BuildPantry(deskRect);
        }

        private void BuildDelivery(RectTransform desk)
        {
            Image area = UIFactory.Panel(desk, "Food Delivery", new Color(0f, 0f, 0f, 0.25f));
            deliveryRect = area.rectTransform;
            UIFactory.Place(deliveryRect, 0.02f, 0.69f, 0.13f, 0.97f);
            deliveryOutline = UIFactory.Outline(deliveryRect, new Color(1f, 1f, 1f, 0.5f), 3f);
            deliveryLabel = UIFactory.Label(deliveryRect, "", 15, TextAnchor.MiddleCenter, FontStyle.Bold, UIFactory.MutedText);
            UIFactory.Stretch(deliveryLabel.rectTransform, 8f);
            UIFactory.FitText(deliveryLabel);
        }

        private DeskSlot BuildDeskSlot(RectTransform desk, float xMin, float yMin, float xMax, float yMax)
        {
            DeskSlot slot = new DeskSlot();
            Image back = UIFactory.Panel(desk, "Desk Space", new Color(0f, 0f, 0f, 0.12f));
            slot.Rect = back.rectTransform;
            UIFactory.Place(slot.Rect, xMin, yMin, xMax, yMax, 4f);
            slot.Content = UIFactory.Rect(slot.Rect, "Content");
            UIFactory.Stretch(slot.Content, 4f);
            slot.Rect.gameObject.AddComponent<DragHandle>().Began = screen => GrabFromSlot(slot, screen);
            return slot;
        }

        private StationView BuildStationTile(RectTransform desk, CookingStation station, string title, float xMin, float xMax)
        {
            StationView view = new StationView { Station = station };
            Image tile = UIFactory.Panel(desk, station.Name, new Color(0.12f, 0.11f, 0.13f, 1f));
            view.Rect = tile.rectTransform;
            UIFactory.Place(view.Rect, xMin, 0.36f, xMax, 0.81f, 4f);
            DragHandle handle = tile.gameObject.AddComponent<DragHandle>();
            handle.Began = screen => GrabStation(view, screen);
            handle.Clicked = () => StartStation(view);

            VerticalLayoutGroup layout = UIFactory.Vertical(tile.gameObject, 4f, 8);
            layout.childForceExpandHeight = false;
            Text header = Header(tile.transform, title);
            UIFactory.Size(header, 18f, 0f);
            UIFactory.FitText(header);
            view.Bar = new TimingBar(tile.transform, station, 16f);
            view.Status = UIFactory.Label(tile.transform, "", 14, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(view.Status, 30f, 0f);
            UIFactory.FitText(view.Status);

            RectTransform potArea = UIFactory.Rect(tile.transform, "Stove");
            UIFactory.Size(potArea, 80f, 1f);
            view.Pot = BuildCookware(potArea, station);

            view.Contents = UIFactory.Label(tile.transform, "", 13);
            UIFactory.Size(view.Contents, 16f, 0f);
            UIFactory.FitText(view.Contents);

            RectTransform row = UIFactory.Row(tile.transform, 22f);
            view.EmptyButton = UIFactory.Button(row, "Take it all out", () => EmptyStation(view), new Color(0.3f, 0.22f, 0.22f, 1f), 13);
            return view;
        }

        private void BuildPlateStack(RectTransform desk)
        {
            Image stack = UIFactory.Panel(desk, "Clean Plates", new Color(0f, 0f, 0f, 0.15f));
            plateStack = stack.rectTransform;
            UIFactory.Place(plateStack, 0.66f, 0.82f, 0.785f, 0.98f, 2f);
            stack.gameObject.AddComponent<DragHandle>().Began = GrabCleanPlate;

            for(int i = 0; i < 3; i++)
            {
                Image disc = UIFactory.Panel(plateStack, "Plate", plateSprite != null ? Color.white : new Color(0.86f, 0.84f, 0.79f, 1f));
                disc.sprite = plateSprite != null ? plateSprite : UIFactory.Circle;
                disc.preserveAspect = true;
                disc.raycastTarget = false;
                UIFactory.Place(disc.rectTransform, 0.08f, 0.22f + i * 0.06f, 0.6f, 0.92f + i * 0.06f);
                disc.rectTransform.anchoredPosition = new Vector2(i * 3f, 0f);
            }

            Text label = UIFactory.Label(plateStack, "Clean\nplates", 13, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.TextColor);
            UIFactory.Stretch(label.rectTransform, 4f);
        }

        private void BuildPantry(RectTransform desk)
        {
            Image pantry = UIFactory.Panel(desk, "Pantry", new Color(0.12f, 0.11f, 0.13f, 1f));
            pantryRect = pantry.rectTransform;
            UIFactory.Place(pantryRect, 0.8f, 0.03f, 0.988f, 0.97f);
            VerticalLayoutGroup layout = UIFactory.Vertical(pantry.gameObject, 4f, 8);
            layout.childForceExpandHeight = false;
            Text header = Header(pantry.transform, "PANTRY  <color=#77736d>hold to take one</color>");
            UIFactory.FitText(header);
            pantryContent = UIFactory.ScrollList(pantry.transform, 4f);
        }

        /// <summary>A small heap of the same picture, for a pile of units.</summary>
        private static void AddPile(RectTransform parent, Sprite sprite, int count, float size, Vector2 anchor)
        {
            for(int i = 0; i < count; i++)
            {
                Image icon = UIFactory.Panel(parent, "Piece", sprite != null ? Color.white : Color.clear);
                icon.sprite = sprite;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                RectTransform rt = icon.rectTransform;
                rt.anchorMin = rt.anchorMax = anchor;
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * size * 0.3f, (i % 2) * size * 0.12f);
            }
        }

        /// <summary>A plate: a disc with what's on it.</summary>
        private void BuildPlateVisual(RectTransform parent, Plate target)
        {
            Image disc = UIFactory.Panel(parent, "Plate", plateSprite != null ? Color.white : new Color(0.86f, 0.84f, 0.79f, 1f));
            disc.sprite = plateSprite != null ? plateSprite : UIFactory.Circle;
            disc.preserveAspect = true;
            disc.raycastTarget = false;
            UIFactory.Place(disc.rectTransform, 0f, 0.12f, 1f, 1f);

            int shown = 0;
            foreach(IngredientPortion portion in target.Items)
            {
                if(shown >= 4) break;
                AddPile(disc.rectTransform, portion.ingredient.Icon, 1, 34f, new Vector2(0.3f + (shown % 2) * 0.4f, 0.65f - (shown / 2) * 0.3f));
                shown++;
            }

            string label = target.Items.Count == 0 ? "Empty plate" : target.HasDish || HasRawFood(target) ? DishLabel(target) : "Seasoning";
            Text text = UIFactory.Label(parent, label, 12, TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(text.rectTransform, -0.1f, -0.06f, 1.1f, 0.2f);
            UIFactory.FitText(text);
        }

        private string DishLabel(Plate target)
        {
            RecipeDefinition match = CustomerRelationship.FindMatch(KnownRecipes(), target.Items);
            return match != null && IsOnMenu(match) ? match.DisplayName : "A dish";
        }

        private void RefreshKitchen()
        {
            if(pantryContent == null) return;

            RebuildPantry();
            foreach(DeskSlot slot in deskSlots) RebuildSlot(slot);
            RefreshBoards();

            RefreshStationContents(wokView);
            RefreshStationContents(potView);
            UpdateStationView(wokView);
            UpdateStationView(potView);

            deliveryLabel.text = phase == Phase.Service && customer != null && orderIn
                ? "FOOD DELIVERY\n<size=12><color=#a6a199>drop a plate here to serve " + customer.name + "</color></size>"
                : "FOOD DELIVERY";

            UIFactory.SetButtonText(previewButton, livePreview ? "Live flavour: on" : "Live flavour: off");
            plateRadar.ClearProfiles();
            // The order only when it's a recipe you know (on the menu).
            if(orderIn && orderKnown) plateRadar.AddProfile(orderProfile, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            if(livePreview && focusPlate != null && focusPlate.Items.Count > 0)
            {
                plateRadar.AddProfile(FlavourCalculator.Calculate(focusPlate.Items, rules), UIFactory.Accent, true);
            }
        }

        private void RebuildSlot(DeskSlot slot)
        {
            UIFactory.ClearChildren(slot.Content);

            if(slot.Plate != null)
            {
                RectTransform holder = UIFactory.Rect(slot.Content, "Plate");
                holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0.5f);
                float size = Mathf.Min(slot.Rect.rect.width, slot.Rect.rect.height) - 12f;
                holder.sizeDelta = new Vector2(Mathf.Max(60f, size), Mathf.Max(60f, size));
                BuildPlateVisual(holder, slot.Plate);
            }
            else if(slot.Loose != null)
            {
                AddPile(slot.Content, slot.Loose.ingredient.Icon, Mathf.Min(slot.Loose.quantity, 3), 46f, new Vector2(0.5f, 0.6f));
                Text label = UIFactory.Label(slot.Content, Describe(slot.Loose, true), 12, TextAnchor.LowerCenter, FontStyle.Bold);
                UIFactory.Stretch(label.rectTransform);
                UIFactory.FitText(label);
            }
        }

        private void RebuildPantry()
        {
            UIFactory.ClearChildren(pantryContent);
            List<Inventory.Entry> entries = inventory.Entries();

            if(entries.Count == 0)
            {
                Text empty = UIFactory.Label(pantryContent, "<color=#a6a199>Empty. Ingredients come from the Shop on the tablet in the morning.</color>", 14);
                UIFactory.Size(empty, 60f, 0f);
                return;
            }

            AddPantryGroup("RAW", entries.FindAll(e => !e.Ingredient.IsSeasoning && e.Prep == PrepSteps.None));
            AddPantryGroup("PREPPED", entries.FindAll(e => !e.Ingredient.IsSeasoning && e.Prep != PrepSteps.None));
            AddPantryGroup("SEASONING", entries.FindAll(e => e.Ingredient.IsSeasoning));
        }

        private void AddPantryGroup(string title, List<Inventory.Entry> entries)
        {
            if(entries.Count == 0) return;

            Text header = Header(pantryContent, title);
            UIFactory.Size(header, 18f, 0f);

            RectTransform grid = UIFactory.Rect(pantryContent, "Piles");
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(116f, 58f);
            layout.spacing = new Vector2(4f, 4f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 2;
            int rows = (entries.Count + 1) / 2;
            UIFactory.Size(grid, rows * 62f, 0f);

            foreach(Inventory.Entry entry in entries)
            {
                Inventory.Entry captured = entry;
                Image cell = UIFactory.Panel(grid, entry.Ingredient.DisplayName, new Color(1f, 1f, 1f, 0.05f));
                cell.gameObject.AddComponent<DragHandle>().Began = screen => GrabFromPantry(captured, screen);

                // Each piece shown, up to a small heap.
                AddPile(cell.rectTransform, entry.Ingredient.Icon, Mathf.Min(entry.Quantity, 3), 34f, new Vector2(0.22f, 0.5f));

                string prep = entry.Prep == PrepSteps.None ? "" : "\n<color=#f28c33>" + PrepLabel(entry.Prep) + "</color>";
                Text label = UIFactory.Label(cell.rectTransform, entry.Ingredient.DisplayName + prep + "\n<color=#a6a199>× " + entry.Quantity + "</color>",
                    12, TextAnchor.MiddleLeft);
                UIFactory.Place(label.rectTransform, 0.44f, 0f, 1f, 1f, 2f);
                UIFactory.FitText(label);
            }
        }

        private static string PrepLabel(PrepSteps prep)
        {
            List<string> steps = new List<string>();
            if((prep & PrepSteps.Peeled) != 0) steps.Add("peeled");
            if((prep & PrepSteps.Sliced) != 0) steps.Add("sliced");
            if((prep & PrepSteps.Grated) != 0) steps.Add("grated");
            return string.Join(", ", steps);
        }

        private void RefreshStationContents(StationView view)
        {
            FillCookware(view.Pot, view.Station);

            if(view.Station.IsEmpty)
            {
                // Short, so it doesn't cover the pot or pan picture.
                view.Contents.text = view.Station.OneIngredientWithoutStock
                    ? "<color=#a6a199>One ingredient, or anything with a stock cube</color>"
                    : "<color=#a6a199>Drop ingredients in</color>";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                if(view.Station.IsSoup) sb.Append("<color=#f28c33>Soup:</color> ");
                foreach(IngredientPortion portion in view.Station.Contents) sb.Append(Describe(portion)).Append("  ");
                view.Contents.text = sb.ToString();
            }

            view.EmptyButton.interactable = !view.Station.IsEmpty && !view.Station.IsCooking;
        }

        private void UpdateStationView(StationView view)
        {
            if(view == null) return;

            CookingStation station = view.Station;
            view.Bar.Set(station.Progress, station.IsCooking);
            view.Pot.Root.SetActive(heldStation != view);
            bool soup = station.IsSoup && station.IsCooking;
            if(view.Pot.Water.gameObject.activeSelf != soup) view.Pot.Water.gameObject.SetActive(soup);

            if(heldStation == view)
            {
                view.Status.text = "Off the heat (paused)";
                view.Status.color = UIFactory.MutedText;
            }
            else if(station.IsCooking && station.Stirs)
            {
                string zone = station.IsInBlack ? "BURNING! Hold and shake" : station.Progress > station.PerfectEnd ? "Red: a stir now overcooks"
                    : station.Progress >= station.PerfectStart ? "Green: hold and shake" : "Heating up";
                view.Status.text = zone + "\n<size=12>" + WokSummary(station) + "</size>";
                view.Status.color = station.IsInBlack ? UIFactory.Bad : station.Progress > station.PerfectEnd ? UIFactory.Accent
                    : station.Progress >= station.PerfectStart ? UIFactory.Good : UIFactory.MutedText;
            }
            else if(station.IsCooking)
            {
                Doneness doneness = station.CurrentDoneness;
                view.Status.text = (station.Progress * 100f).ToString("0") + "%  " + DonenessLabel(doneness)
                                   + (doneness == Doneness.Perfect ? "\n<size=12>hold and drop on a plate</size>" : "");
                view.Status.color = doneness == Doneness.Perfect ? UIFactory.Good : doneness == Doneness.Undercooked ? UIFactory.MutedText : UIFactory.Bad;
            }
            else
            {
                view.Status.text = station.IsEmpty ? "Off" : "Loaded. Click to light it.";
                view.Status.color = station.IsEmpty ? UIFactory.MutedText : UIFactory.Accent;
            }
        }
    }
}
