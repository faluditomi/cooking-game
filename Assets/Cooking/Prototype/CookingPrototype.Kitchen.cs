using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The workbench (service only). Everything is press-and-hold, as on the Miro mock-up, with the
    /// Notion "Demo Changes" on top:
    /// <list type="bullet">
    /// <item><b>Pantry</b>: ingredients piled by kind (three columns). Hold one to take a single unit;
    /// drop it on a cutting board, the pot, the pan, a plate (seasonings, or anything raw on a clean
    /// plate) or free desk space. Anywhere else it goes back on its pile.</item>
    /// <item><b>Desk</b>: no grid. Plates and ingredients are dropped where you let go, inside the free
    /// area, as long as they don't land on something else (they slide to the nearest free spot). A
    /// clean plate can only be taken when there's room for it.</item>
    /// <item><b>Pot and pan</b> sit on stoves. They start cooking with the first thing in. Hold one to
    /// lift it (empty or not; a lifted one pauses): drop it on a plate to dish up, on the pan to pour the
    /// pot in, over the pantry to tip everything back, anywhere else to put it back. The pan is shaken
    /// while lifted; the pot is stirred with the spoon.</item>
    /// <item><b>Food delivery</b>: dropping a plate there serves it.</item>
    /// </list>
    /// </summary>
    public partial class CookingPrototype
    {
        /// <summary>Plates and loose ingredients on the desk, in canvas pixels.</summary>
        private const float PlateSize = 100f;
        private const float LooseSize = 76f;
        /// <summary>Direction changes for one counted shake of the pan (one move to the side isn't a shake).</summary>
        private const int ShakeStrokes = 4;

        /// <summary>The free desk area (desk-normalised rectangles): a band along the top, and the corner above the tablet.</summary>
        private static readonly Rect[] FreeArea =
        {
            new Rect(0.12f, 0.8f, 0.455f, 0.18f),
            new Rect(0.12f, 0.6f, 0.265f, 0.2f),
        };

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

        /// <summary>Something on the desk: one plate, or loose units of one kind, where it was put down.</summary>
        private class DeskItem
        {
            /// <summary>Centre, in desk-local pixels from the desk's bottom left.</summary>
            public Vector2 Position;
            public RectTransform Rect;
            public Plate Plate;
            public IngredientPortion Loose;
            public float Size => Plate != null ? PlateSize : LooseSize;
        }

        private class StationView
        {
            public CookingStation Station;
            /// <summary>The stove: where things are dropped in, and where the pot or pan sits.</summary>
            public RectTransform Rect;
            public Image Burner;
            public Cookware Pot;
            /// <summary>Ingredient cost of what's in it, passed on to the plate.</summary>
            public float Cost;
            public CookingStation.Stage LastStage;
        }

        /// <summary>A drawn pot or pan: the picture, the pieces inside, water for a soup, and its little bar.</summary>
        private class Cookware
        {
            public GameObject Root;
            public RectTransform Inside;
            public Image Water;
            public TimingBar Bar;
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
            Desk,
            Stack,
            Stove,
            Hook,
        }

        private CookingStation wok;
        private CookingStation pot;
        private Gesture shakeGesture;
        /// <summary>The plate being served, for judging and the result screen.</summary>
        private readonly List<IngredientPortion> plate = new List<IngredientPortion>();
        private readonly List<DeskItem> deskSlots = new List<DeskItem>();
        /// <summary>The plate the order page's radar shows: the last one something went onto.</summary>
        private Plate focusPlate;

        private StationView wokView;
        private StationView potView;
        private RectTransform deskItemsLayer;
        private RectTransform pantryRect;
        private RectTransform pantryContent;
        private RectTransform plateStack;
        private RectTransform deliveryRect;
        private readonly List<Image> deliveryDots = new List<Image>();
        private FlavourRadarGraphic plateRadar;

        // What's in hand
        private Held held;
        private Origin heldFrom;
        private int heldIndex;
        private Vector2 heldFromPosition;
        private IngredientPortion heldPortion;
        private Plate heldPlate;
        private StationView heldStation;
        private int shakeStrokes;
        private RectTransform ghost;
        /// <summary>The pot or pan in hand (its picture and bar), so a shake shows straight away.</summary>
        private Cookware liftedCookware;

        private void CreateKitchenState()
        {
            wok = CookingStation.CreateWok(wokSeconds);
            pot = CookingStation.CreatePot(potSeconds);
            shakeGesture = new BackAndForthGesture(vertical: false, requiredStrokes: int.MaxValue, minTravel: 45f);
        }

        private void TickKitchen(float deltaTime)
        {
            // Lifted off the stove, a pot or pan pauses and carries on where it left off when it's put back.
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

            foreach(DeskItem item in deskSlots) if(item.Rect != null) Destroy(item.Rect.gameObject);
            deskSlots.Clear();

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

            foreach(DeskItem item in deskSlots)
            {
                if(item.Loose == null) continue;
                inventory.Add(item.Loose.ingredient, item.Loose.prep, item.Loose.quantity);
                item.Loose = null;
            }

            RemoveEmptyItems();

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
            foreach(DeskItem item in deskSlots) if(item.Loose != null) return true;
            foreach(CuttingBoard board in boards) if(board.Units != null) return true;
            return false;
        }

        /// <summary>Units of <paramref name="ingredient"/> on the desk and boards, all of them or just those prepped as <paramref name="prep"/>.</summary>
        private int OnDesk(IngredientDefinition ingredient, PrepSteps? prep = null)
        {
            int count = 0;

            foreach(DeskItem item in deskSlots)
            {
                if(item.Loose != null && item.Loose.ingredient == ingredient && (prep == null || item.Loose.prep == prep)) count += item.Loose.quantity;
            }

            foreach(CuttingBoard board in boards)
            {
                if(board.Units != null && board.Units.ingredient == ingredient && (prep == null || board.Units.prep == prep)) count += board.Units.quantity;
            }

            return count;
        }

        /// <summary>"in the pot" / "in the pan" when that station holds at least <paramref name="quantity"/> prepped like this, or null.</summary>
        private string InStation(IngredientDefinition ingredient, PrepSteps prep, int quantity)
        {
            foreach(CookingStation station in new[] { pot, wok })
            {
                int count = 0;
                foreach(IngredientPortion portion in station.Contents)
                {
                    if(portion.ingredient == ingredient && portion.prep == prep) count += portion.quantity;
                }

                if(count >= quantity) return station == pot ? "in the pot" : "in the pan";
            }

            return null;
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

        private void GrabFromDesk(DeskItem item, Vector2 screen)
        {
            if(!CanPickUp || !deskSlots.Contains(item)) return;

            heldFromPosition = item.Position;
            Plate takenPlate = item.Plate;
            IngredientPortion takenLoose = item.Loose;
            item.Plate = null;
            item.Loose = null;
            RemoveEmptyItems();

            if(takenPlate != null) HoldPlate(takenPlate, Origin.Desk, 0, screen);
            else if(takenLoose != null) HoldPortion(takenLoose, Origin.Desk, 0, screen);
        }

        private void GrabCleanPlate(Vector2 screen)
        {
            if(!CanPickUp) return;

            // Only when there's somewhere to put it.
            if(!HasRoom(PlateSize))
            {
                Say("There's no room on the desk for another plate.", UIFactory.Bad);
                return;
            }

            HoldPlate(new Plate(), Origin.Stack, 0, screen);
        }

        private void GrabStation(StationView view, Vector2 screen)
        {
            if(!CanPickUp) return;

            held = Held.Station;
            heldFrom = Origin.Stove;
            heldStation = view;
            shakeStrokes = 0;

            Play(sounds.pickup, 0.7f, 0.05f, 0.8f);
            // The pot or pan, with its little bar still on it.
            CreateGhost(screen, 220f, 240f);
            Cookware lifted = BuildCookware(ghost, view.Station);
            FillCookware(lifted, view.Station);
            lifted.Bar.Set(view.Station.Progress, view.Station.IsCooking);
            liftedCookware = lifted;

            if(view.Station == wok) shakeGesture.Begin(CanvasPoint(screen));
            RefreshAll();
        }

        private void HoldPortion(IngredientPortion portion, Origin from, int index, Vector2 screen)
        {
            held = Held.Portion;
            heldFrom = from;
            heldIndex = index;
            heldPortion = portion;

            Play(sounds.pickup, 0.6f);
            CreateGhost(screen, 150f, 90f);
            AddPile(ghost, PrepSprites.For(portion.ingredient, portion.prep), Mathf.Min(portion.quantity, 3), 52f, new Vector2(0.5f, 0.62f));
            // Its name above it while it's held (always, whatever the pantry labels are set to).
            Text label = UIFactory.Label(ghost, IngredientLabel(portion.ingredient, portion.quantity, portion.prep), 14, TextAnchor.LowerCenter, FontStyle.Bold);
            UIFactory.Place(label.rectTransform, -0.4f, 0.95f, 1.4f, 1.3f);
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

            Play(sounds.plate, 0.35f, 0.08f, 1.3f);
            CreateGhost(screen, PlateSize, PlateSize);
            BuildPlateVisual(ghost, taken);
            RefreshAll();
        }

        // ------------------------------------------------------------------ Holding and dropping

        /// <summary>Follows the pointer while something is held; drops it when the button comes up.</summary>
        private void UpdateHeld()
        {
            UpdateDeliveryDots();

            if(held == Held.None)
            {
                // Let go: the dotted preview goes.
                if(hoverTarget != null) UpdateTastePreview(Vector2.zero);
                return;
            }

            Pointer pointer = Pointer.current;
            Vector2 position = pointer != null ? pointer.position.ReadValue() : Vector2.zero;

            if(pointer == null || !pointer.press.isPressed)
            {
                DropHeld(position);
                return;
            }

            MoveGhost(position);
            UpdateTastePreview(position);

            if(held == Held.Station)
            {
                if(heldStation.Station == wok) ShakeHeld(position);
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

            switch(what)
            {
                case Held.Portion: DropPortion(screen); Play(sounds.place, 0.6f); break;
                case Held.Plate: DropPlate(screen); Play(sounds.plate, 0.6f); break;
                case Held.Station: DropStation(screen); Play(sounds.place, 0.8f, 0.04f, 0.8f); break;
                case Held.Tool: DropTool(); Play(sounds.place, 0.3f, 0.05f, 1.6f); break;
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
                return;
            }

            DeskItem item = ItemAt(screen);

            if(item != null)
            {
                if(item.Plate != null)
                {
                    if(!Season(item.Plate, portion)) ReturnPortion(portion);
                    return;
                }

                if(SameKind(item.Loose, portion))
                {
                    item.Loose.quantity += portion.quantity;
                    return;
                }
            }

            // Free desk space: where it's let go, or the nearest free spot to it.
            if(Over(deskRect, screen) && !Over(pantryRect, screen) && TryPlace(DeskPoint(screen), LooseSize, out Vector2 spot))
            {
                AddDeskItem(spot, null, portion);
                return;
            }

            if(Over(deliveryRect, screen)) Say("Only a plate goes there: put it on a plate first.", UIFactory.Bad);

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

            if(heldFrom == Origin.Desk && TryPlace(heldFromPosition, LooseSize, out Vector2 spot))
            {
                AddDeskItem(spot, null, portion);
                return;
            }

            inventory.Add(portion.ingredient, portion.prep, portion.quantity);
        }

        private static bool SameKind(IngredientPortion a, IngredientPortion b)
        {
            return a != null && b != null && a.ingredient == b.ingredient && a.prep == b.prep && a.cooking == b.cooking
                   && a.IsRaw == b.IsRaw && (a.IsRaw || a.doneness == b.doneness);
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
                Say(portion.ingredient.DisplayName + " can't go on a cooked dish: everything for a dish is cooked in the pot or the pan, only seasonings go on after.", UIFactory.Bad);
                return false;
            }

            AddToPlate(target, portion);
            target.Cost += portion.ingredient.Price * portion.quantity;
            focusPlate = target;
            LogCooking("Plate", (target.HasDish ? "Seasoned with " : "Put on raw: ") + ListPortions(new[] { portion }) + ".");
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

            // Anywhere on the desk: where it's let go, or the nearest free spot.
            if(Over(deskRect, screen) && TryPlace(DeskPoint(screen), PlateSize, out Vector2 spot))
            {
                AddDeskItem(spot, dropped, null);
                return;
            }

            ReturnPlate(dropped);
        }

        private void ReturnPlate(Plate dropped)
        {
            if(dropped == null || heldFrom == Origin.Stack) return;
            if(TryPlace(heldFromPosition, PlateSize, out Vector2 spot)) AddDeskItem(spot, dropped, null);
        }

        private void DropStation(Vector2 screen)
        {
            StationView view = heldStation;
            heldStation = null;
            shakeGesture.End();

            DeskItem item = ItemAt(screen);

            if(item != null && item.Plate != null)
            {
                if(!view.Station.IsEmpty) DishUp(view, item.Plate);
                return;
            }

            // The pot poured into the pan (boiled rice for fried rice), or the other way round.
            StationView other = StationAt(screen);

            if(other != null && other != view)
            {
                Pour(view, other);
                return;
            }

            // Anywhere else (the pantry included) it just goes back on its stove, still cooking:
            // nothing in it is lost by letting go in the wrong place.
        }

        /// <summary>Everything in <paramref name="from"/>, cooked as it is now, into <paramref name="to"/>.</summary>
        private void Pour(StationView from, StationView to)
        {
            if(from.Station.IsEmpty) return;

            foreach(IngredientPortion portion in from.Station.Contents)
            {
                if(!to.Station.CanAdd(portion, out string reason))
                {
                    Say(reason, UIFactory.Bad);
                    return;
                }
            }

            string what = ListPortions(from.Station.Contents);
            foreach(IngredientPortion cooked in from.Station.TakeOff()) to.Station.Add(cooked);
            to.Cost += from.Cost;
            from.Cost = 0f;
            LogCooking(to.Station.Name, "Poured in from the " + from.Station.Name.ToLowerInvariant() + ": " + what + ".");
        }

        /// <summary>Everything in the station onto <paramref name="target"/>, done as the bar says.</summary>
        private void DishUp(StationView view, Plate target)
        {
            // One dish per plate: no more cooked food once there's a dish on it.
            if(target.HasDish || HasRawFood(target))
            {
                Say((target.HasDish ? "There's already a dish on that plate." : "There's raw food on that plate.") + " Use a clean one.", UIFactory.Bad);
                return;
            }

            CookingStation station = view.Station;
            Doneness doneness = station.CurrentDoneness;
            List<IngredientPortion> cooked = station.TakeOff();
            if(cooked.Count == 0) return;

            foreach(IngredientPortion portion in cooked) AddToPlate(target, portion);
            target.Cost += view.Cost;
            view.Cost = 0f;
            focusPlate = target;
            LogCooking(station.Name, "Dished up: " + DonenessLabel(doneness) + ".");
        }

        /// <summary>The lifted pan, shaken: a few moves side to side make one shake, which holds it in the orange.</summary>
        private void ShakeHeld(Vector2 screen)
        {
            if(!shakeGesture.Move(CanvasPoint(screen))) return;

            Play(sounds.shake, 0.45f, 0.12f);
            if(++shakeStrokes < ShakeStrokes) return;

            shakeStrokes = 0;
            if(!wok.Settle()) return;

            Play(sounds.stir, 0.5f, 0.03f);
            // The pan in hand shows it at once: the bar back at the start of the orange, the food browner.
            if(liftedCookware != null)
            {
                liftedCookware.Bar.Set(wok.Progress, wok.IsCooking);
                FillCookware(liftedCookware, wok);
            }
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

            Play(sounds.serve, 0.8f, 0f);
            JudgeResult result = DishJudge.Judge(order, plate, customer, zones, rules, JudgeSettings());
            if(customer.alwaysPaysAsStranger && Relationship(customer).Stage == NpcStage.Stranger) result = DishJudge.PayInFull(result);
            FinishOrder(result);
            return true;
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
                string prep = PrepLabel(portion.prep);
                if(prep.Length > 0) item += " (" + prep + ")";
                items.Add(item);
            }

            return items.Count > 0 ? string.Join(", ", items) : "nothing";
        }

        // ------------------------------------------------------------------ The desk

        /// <summary>A screen point in desk-local pixels (from the desk's bottom left).</summary>
        private Vector2 DeskPoint(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(deskRect, screen, null, out Vector2 local);
            return local - deskRect.rect.min;
        }

        private Rect FreeAreaPixels(int index)
        {
            Rect desk = deskRect.rect;
            Rect r = FreeArea[index];
            return new Rect(r.x * desk.width, r.y * desk.height, r.width * desk.width, r.height * desk.height);
        }

        /// <summary>A desk spot where something this big fits: inside the free area, not on anything else.</summary>
        private bool Fits(Vector2 centre, float size, DeskItem ignore = null)
        {
            float half = size * 0.5f;
            bool inside = false;

            for(int i = 0; i < FreeArea.Length && !inside; i++)
            {
                Rect area = FreeAreaPixels(i);
                inside = centre.x - half * 0.8f >= area.xMin && centre.x + half * 0.8f <= area.xMax
                         && centre.y - half * 0.8f >= area.yMin && centre.y + half * 0.8f <= area.yMax;
            }

            if(!inside) return false;

            foreach(DeskItem item in deskSlots)
            {
                if(item == ignore) continue;
                // A little overlap is fine; it's a busy table.
                float gap = (item.Size + size) * 0.5f * 0.85f;
                if((item.Position - centre).sqrMagnitude < gap * gap) return false;
            }

            return true;
        }

        /// <summary>Where something dropped at <paramref name="wanted"/> ends up: there, or the nearest free spot.</summary>
        private bool TryPlace(Vector2 wanted, float size, out Vector2 spot)
        {
            if(Fits(wanted, size))
            {
                spot = wanted;
                return true;
            }

            // Spiral outwards for the nearest free spot.
            for(float radius = 10f; radius < 700f; radius += 10f)
            {
                int steps = Mathf.Max(8, (int)(radius / 6f));

                for(int i = 0; i < steps; i++)
                {
                    float angle = i * Mathf.PI * 2f / steps;
                    Vector2 candidate = wanted + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

                    if(Fits(candidate, size))
                    {
                        spot = candidate;
                        return true;
                    }
                }
            }

            spot = wanted;
            return false;
        }

        private bool HasRoom(float size)
        {
            for(int i = 0; i < FreeArea.Length; i++)
            {
                Rect area = FreeAreaPixels(i);
                for(float x = area.xMin + size * 0.4f; x <= area.xMax - size * 0.4f; x += 12f)
                {
                    for(float y = area.yMin + size * 0.4f; y <= area.yMax - size * 0.4f; y += 12f)
                    {
                        if(Fits(new Vector2(x, y), size)) return true;
                    }
                }
            }

            return false;
        }

        private DeskItem AddDeskItem(Vector2 position, Plate newPlate, IngredientPortion loose)
        {
            DeskItem item = new DeskItem { Position = position, Plate = newPlate, Loose = loose };
            deskSlots.Add(item);

            Image hit = UIFactory.Panel(deskItemsLayer, newPlate != null ? "Plate" : "Ingredients", Color.clear);
            hit.sprite = UIFactory.Circle;
            item.Rect = hit.rectTransform;
            item.Rect.anchorMin = item.Rect.anchorMax = Vector2.zero;
            item.Rect.sizeDelta = new Vector2(item.Size, item.Size);
            item.Rect.anchoredPosition = position;
            hit.gameObject.AddComponent<DragHandle>().Began = screen => GrabFromDesk(item, screen);
            return item;
        }

        private void RemoveEmptyItems()
        {
            for(int i = deskSlots.Count - 1; i >= 0; i--)
            {
                if(deskSlots[i].Plate != null || deskSlots[i].Loose != null) continue;
                if(deskSlots[i].Rect != null) Destroy(deskSlots[i].Rect.gameObject);
                deskSlots.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------------ Hit tests

        private static bool Over(RectTransform rect, Vector2 screen)
        {
            return rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null);
        }

        /// <summary>The desk item under the pointer (the nearest, if they overlap).</summary>
        private DeskItem ItemAt(Vector2 screen)
        {
            if(deskRect == null) return null;
            Vector2 point = DeskPoint(screen);
            DeskItem best = null;
            float bestDistance = float.MaxValue;

            foreach(DeskItem item in deskSlots)
            {
                float distance = (item.Position - point).sqrMagnitude;
                float reach = item.Size * 0.55f;

                if(distance < reach * reach && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = item;
                }
            }

            return best;
        }

        private StationView StationAt(Vector2 screen)
        {
            if(Over(wokView.Rect, screen)) return wokView;
            if(Over(potView.Rect, screen)) return potView;
            return null;
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
            liftedCookware = null;
        }

        /// <summary>The delivery spot's dotted outline: pulses green while a plate with food is in hand.</summary>
        private void UpdateDeliveryDots()
        {
            if(deliveryDots.Count == 0) return;

            bool food = held == Held.Plate && heldPlate != null && heldPlate.Items.Count > 0;
            Color rest = new Color(0f, 0f, 0f, 0.4f);
            Color green = new Color(0.35f, 0.85f, 0.4f, 0.95f);
            Color color = food ? Color.Lerp(rest, green, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f)) : rest;
            foreach(Image dot in deliveryDots) dot.color = color;
        }

        // ------------------------------------------------------------------ UI

        /// <summary>
        /// The pot or pan, drawn to fit <paramref name="area"/>: its picture, a dark inside (the pot),
        /// the pieces in it, water for a soup, and its small bar on top (which goes with it when lifted).
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

            // A small bar just above the bowl.
            RectTransform barArea = UIFactory.Rect(root, "Bar");
            UIFactory.Place(barArea, 0.2f, 0.98f, 0.8f, 1.06f);
            VerticalLayoutGroup barLayout = UIFactory.Vertical(barArea.gameObject, 0f, 0);
            barLayout.childForceExpandHeight = true;
            cookware.Bar = new TimingBar(barArea, station, 10f);
            return cookware;
        }

        /// <summary>The station's ingredients, heaped inside the pot or pan: browner once done, nearly black when burnt.</summary>
        private static void FillCookware(Cookware cookware, CookingStation station)
        {
            UIFactory.ClearChildren(cookware.Inside);

            List<Sprite> pieces = new List<Sprite>();
            foreach(IngredientPortion portion in station.Contents)
            {
                for(int i = 0; i < portion.quantity && pieces.Count < 12; i++) pieces.Add(PrepSprites.For(portion.ingredient, portion.prep));
            }

            AddHeap(cookware.Inside, pieces, StageTint(station.CurrentStage), 0.36f);
            cookware.Water.gameObject.SetActive(station.IsSoup && station.IsCooking);
            cookware.Water.transform.SetAsLastSibling();
        }

        private static Color StageTint(CookingStation.Stage stage)
        {
            switch(stage)
            {
                case CookingStation.Stage.Done: return new Color(0.86f, 0.7f, 0.52f, 1f);
                case CookingStation.Stage.Ruined: return new Color(0.2f, 0.16f, 0.14f, 1f);
                default: return Color.white;
            }
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
            potView = BuildStove(deskRect, pot, "Boil", 0.395f, 0.54f);
            wokView = BuildStove(deskRect, wok, "Fry", 0.545f, 0.69f);
            BuildPlateStack(deskRect);
            BuildBoards(deskRect);
            BuildPantry(deskRect);

            // Plates and loose ingredients sit on top of everything else on the desk.
            deskItemsLayer = UIFactory.Rect(deskRect, "On The Desk");
            UIFactory.Stretch(deskItemsLayer);
        }

        /// <summary>The food delivery spot: no text, just a dotted outline the size of one and a half plates.</summary>
        private void BuildDelivery(RectTransform desk)
        {
            RectTransform area = UIFactory.Rect(desk, "Food Delivery");
            deliveryRect = area;
            area.anchorMin = area.anchorMax = new Vector2(0.065f, 0.84f);
            area.sizeDelta = new Vector2(PlateSize * 1.5f, PlateSize * 1.5f);
            // A clear graphic, so it can be pointed at (the tutorial outlines it).
            area.gameObject.AddComponent<Image>().color = Color.clear;

            const int perSide = 9;
            for(int side = 0; side < 4; side++)
            {
                for(int i = 0; i < perSide; i++)
                {
                    float t = (i + 0.5f) / perSide;
                    Vector2 anchor = side == 0 ? new Vector2(t, 0f) : side == 1 ? new Vector2(t, 1f) : side == 2 ? new Vector2(0f, t) : new Vector2(1f, t);
                    Image dot = UIFactory.Panel(area, "Dot", new Color(0f, 0f, 0f, 0.4f));
                    dot.raycastTarget = false;
                    dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = anchor;
                    dot.rectTransform.sizeDelta = new Vector2(7f, 7f);
                    deliveryDots.Add(dot);
                }
            }
        }

        /// <summary>A stove (a hob with one burner) with the pot or pan on it, and a taped-on paper label.</summary>
        private StationView BuildStove(RectTransform desk, CookingStation station, string label, float xMin, float xMax)
        {
            StationView view = new StationView { Station = station };

            // A drawn camping stove: a steel top with a darker front strip and two knobs.
            Image stove = UIFactory.Panel(desk, station.Name + " Stove", new Color(0.62f, 0.63f, 0.66f, 1f));
            UIFactory.SkinPanel(stove, stove.color);
            view.Rect = stove.rectTransform;
            UIFactory.Place(view.Rect, xMin, 0.29f, xMax, 0.79f, 4f);
            stove.gameObject.AddComponent<DragHandle>().Began = screen => GrabStation(view, screen);

            Image front = UIFactory.Panel(view.Rect, "Front", new Color(0.32f, 0.33f, 0.36f, 1f));
            front.raycastTarget = false;
            UIFactory.Place(front.rectTransform, 0f, 0f, 1f, 0.1f);
            for(int k = 0; k < 2; k++)
            {
                Image knob = UIFactory.Panel(front.rectTransform, "Knob", new Color(0.1f, 0.1f, 0.11f, 1f));
                knob.sprite = UIFactory.Circle;
                knob.preserveAspect = true;
                knob.raycastTarget = false;
                UIFactory.Place(knob.rectTransform, 0.06f + k * 0.12f, 0.12f, 0.16f + k * 0.12f, 0.88f);
            }

            // The burner: a dark ring that glows while it's on.
            Image ring = UIFactory.Panel(view.Rect, "Burner", new Color(0.08f, 0.08f, 0.09f, 1f));
            ring.sprite = UIFactory.Circle;
            ring.preserveAspect = true;
            ring.raycastTarget = false;
            UIFactory.Place(ring.rectTransform, 0.12f, 0.12f, 0.88f, 0.82f);
            view.Burner = UIFactory.Panel(ring.rectTransform, "Glow", new Color(1f, 0.45f, 0.1f, 0f));
            view.Burner.sprite = UIFactory.Circle;
            view.Burner.preserveAspect = true;
            view.Burner.raycastTarget = false;
            UIFactory.Stretch(view.Burner.rectTransform, 10f);

            RectTransform potArea = UIFactory.Rect(view.Rect, "On The Stove");
            UIFactory.Place(potArea, 0.02f, 0.11f, 0.98f, 0.9f);
            view.Pot = BuildCookware(potArea, station);

            // A scrap of paper, taped on at the bottom right.
            RectTransform paperRect = PaperNote(view.Rect, label, 18, station == wok ? 4f : -5f);
            paperRect.anchorMin = paperRect.anchorMax = new Vector2(1f, 0f);
            paperRect.pivot = new Vector2(1f, 0f);
            paperRect.sizeDelta = new Vector2(66f, 30f);
            paperRect.anchoredPosition = new Vector2(-6f, 8f);

            view.LastStage = CookingStation.Stage.Raw;
            return view;
        }

        /// <summary>A scrap of paper with a word written on it and a strip of tape on top (the stove and pantry labels). Size and place it after.</summary>
        private static RectTransform PaperNote(Transform parent, string text, int fontSize, float tilt)
        {
            Image paper = UIFactory.Panel(parent, "Label", new Color(0.93f, 0.9f, 0.8f, 1f));
            paper.raycastTarget = false;
            RectTransform paperRect = paper.rectTransform;
            paperRect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            Text written = UIFactory.Label(paperRect, text, fontSize, TextAnchor.MiddleCenter, FontStyle.Italic, new Color(0.15f, 0.13f, 0.12f, 1f));
            written.raycastTarget = false;
            UIFactory.Stretch(written.rectTransform);
            Image tape = UIFactory.Panel(paperRect, "Tape", new Color(0.75f, 0.75f, 0.72f, 0.6f));
            tape.raycastTarget = false;
            tape.rectTransform.anchorMin = tape.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            tape.rectTransform.sizeDelta = new Vector2(34f, 11f);
            tape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -8f);
            return paperRect;
        }

        /// <summary>The stack of clean plates: just plates, outlined and shaded so they read as plates.</summary>
        private void BuildPlateStack(RectTransform desk)
        {
            RectTransform stack = UIFactory.Rect(desk, "Clean Plates");
            plateStack = stack;
            UIFactory.Place(plateStack, 0.585f, 0.8f, 0.69f, 0.985f);
            Image hit = stack.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            stack.gameObject.AddComponent<DragHandle>().Began = GrabCleanPlate;

            for(int i = 0; i < 4; i++)
            {
                RectTransform one = UIFactory.Rect(plateStack, "Plate");
                UIFactory.Place(one, 0.12f, 0.02f + i * 0.05f, 0.88f, 0.82f + i * 0.05f);
                one.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                PlateImage(one);
            }
        }

        /// <summary>The pantry: no panel, no title; piles in three columns, grouped raw / prepped / seasoning.</summary>
        private void BuildPantry(RectTransform desk)
        {
            RectTransform pantry = UIFactory.Rect(desk, "Pantry");
            pantryRect = pantry;
            UIFactory.Place(pantryRect, 0.7f, 0.02f, 0.995f, 0.98f);
            // A clear graphic: so the pantry can be pointed at, and things dropped over it.
            pantry.gameObject.AddComponent<Image>().color = Color.clear;
            VerticalLayoutGroup layout = UIFactory.Vertical(pantry.gameObject, 2f, 4);
            layout.childForceExpandHeight = false;
            pantryContent = UIFactory.ScrollList(pantry, 2f);
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

        /// <summary>A plate on the desk or in hand: the dish picture.</summary>
        private void BuildPlateVisual(RectTransform parent, Plate target)
        {
            RectTransform picture = UIFactory.Rect(parent, "Dish");
            UIFactory.Stretch(picture);
            BuildDishPicture(picture, target.Items);
        }

        private void RefreshKitchen()
        {
            if(pantryContent == null) return;

            RebuildPantry();
            foreach(DeskItem item in deskSlots) RebuildDeskItem(item);
            RefreshBoards();

            RefreshStationContents(wokView);
            RefreshStationContents(potView);
            UpdateStationView(wokView);
            UpdateStationView(potView);

            // The order radar as a cooking guide (CookingPrototype.Taste.cs).
            RefreshTasteGuide();
        }

        private void RebuildDeskItem(DeskItem item)
        {
            UIFactory.ClearChildren(item.Rect);
            item.Rect.sizeDelta = new Vector2(item.Size, item.Size);
            item.Rect.anchoredPosition = item.Position;

            if(item.Plate != null)
            {
                BuildPlateVisual(item.Rect, item.Plate);
            }
            else if(item.Loose != null)
            {
                AddPile(item.Rect, PrepSprites.For(item.Loose.ingredient, item.Loose.prep), Mathf.Min(item.Loose.quantity, 3), item.Size * 0.62f, new Vector2(0.5f, 0.55f));

                if(ingredientLabels)
                {
                    Text label = UIFactory.Label(item.Rect, IngredientLabel(item.Loose.ingredient, item.Loose.quantity, item.Loose.prep), 12, TextAnchor.UpperCenter,
                        FontStyle.Bold);
                    UIFactory.Place(label.rectTransform, -0.3f, -0.25f, 1.3f, 0.05f);
                }
            }
        }

        /// <summary>"x2 Pak Choi", with its prep: the pantry and desk labels.</summary>
        private static string IngredientLabel(IngredientDefinition ingredient, int quantity, PrepSteps prep)
        {
            string label = "x" + quantity + " " + ingredient.DisplayName;
            string steps = PrepLabel(prep);
            return steps.Length > 0 ? label + " <color=#f2b066>(" + steps + ")</color>" : label;
        }

        private void RebuildPantry()
        {
            UIFactory.ClearChildren(pantryContent);
            pantryPiles.Clear();
            List<Inventory.Entry> entries = inventory.Entries();

            // The three groups are always there, labelled like the stoves, even when one is empty.
            AddPantryGroup("RAW", entries.FindAll(e => !e.Ingredient.IsSeasoning && e.Prep == PrepSteps.None));
            AddPantryGroup("PREPPED", entries.FindAll(e => !e.Ingredient.IsSeasoning && e.Prep != PrepSteps.None));
            AddPantryGroup("SEASONING", entries.FindAll(e => e.Ingredient.IsSeasoning));
        }

        private void AddPantryGroup(string title, List<Inventory.Entry> entries)
        {
            // A taped paper label, like the ones on the stoves.
            RectTransform headerRow = UIFactory.Rect(pantryContent, title);
            UIFactory.Size(headerRow, 34f, 0f);
            RectTransform note = PaperNote(headerRow, title.Substring(0, 1) + title.Substring(1).ToLowerInvariant(), 16, title.Length % 2 == 0 ? -3f : 3f);
            note.anchorMin = note.anchorMax = new Vector2(0f, 0.5f);
            note.pivot = new Vector2(0f, 0.5f);
            note.sizeDelta = new Vector2(16f + title.Length * 10f, 26f);
            note.anchoredPosition = new Vector2(6f, 0f);

            const int columns = 4;
            RectTransform grid = UIFactory.Rect(pantryContent, "Piles");
            GridLayoutGroup layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(104f, ingredientLabels ? 64f : 50f);
            layout.spacing = new Vector2(4f, 2f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;
            int rows = (entries.Count + columns - 1) / columns;
            UIFactory.Size(grid, rows * (layout.cellSize.y + 2f), 0f);

            foreach(Inventory.Entry entry in entries)
            {
                Inventory.Entry captured = entry;
                // A clear cell: just the pile (and its label) on the table.
                Image cell = UIFactory.Panel(grid, entry.Ingredient.DisplayName, Color.clear);
                cell.gameObject.AddComponent<DragHandle>().Began = screen => GrabFromPantry(captured, screen);
                if(entry.Prep == PrepSteps.None) pantryPiles[entry.Ingredient] = cell.rectTransform;

                // Each piece shown, up to a small heap.
                AddPile(cell.rectTransform, PrepSprites.For(entry.Ingredient, entry.Prep), Mathf.Min(entry.Quantity, 4), 34f, new Vector2(0.5f, ingredientLabels ? 0.62f : 0.5f));

                if(ingredientLabels)
                {
                    Text label = UIFactory.Label(cell.rectTransform, IngredientLabel(entry.Ingredient, entry.Quantity, entry.Prep), 11, TextAnchor.LowerCenter);
                    UIFactory.Place(label.rectTransform, 0f, 0f, 1f, 0.32f);
                    UIFactory.FitText(label);
                }
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
        }

        private void UpdateStationView(StationView view)
        {
            if(view == null) return;

            CookingStation station = view.Station;
            bool onStove = heldStation != view;
            view.Pot.Root.SetActive(onStove);
            view.Pot.Bar.Set(station.Progress, station.IsCooking);

            // The burner glows while something's cooking on it.
            float glow = onStove && station.IsCooking ? 0.35f + 0.1f * Mathf.Sin(Time.unscaledTime * 3f) : 0f;
            view.Burner.color = new Color(1f, 0.45f, 0.1f, glow);

            bool soup = station.IsSoup && station.IsCooking;
            if(view.Pot.Water.gameObject.activeSelf != soup) view.Pot.Water.gameObject.SetActive(soup);

            // The food changes colour as it goes from blue to orange to red.
            CookingStation.Stage stage = station.CurrentStage;

            if(stage != view.LastStage)
            {
                if(stage == CookingStation.Stage.Ruined) Play(sounds.burn, 0.7f);
                view.LastStage = stage;
                FillCookware(view.Pot, station);
            }
        }
    }
}
