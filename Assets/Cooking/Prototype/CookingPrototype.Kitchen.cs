using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The Kitchen area (service only): move stock into the wok, the pot or straight onto the plate,
    /// cook, plate and serve against the order timer.
    /// </summary>
    public partial class CookingPrototype
    {
        private enum Destination
        {
            Wok,
            Pot,
            Plate,
        }

        private class StationView
        {
            public CookingStation Station;
            public Text Contents;
            public Text Status;
            public TimingBar Bar;
            public Button StartButton;
            public Button TakeOffButton;
            public Button BinButton;
            public Text PadHint;
            public bool? WasInDanger;
        }

        private CookingStation wok;
        private CookingStation pot;
        private Gesture shakeGesture;
        private readonly List<IngredientPortion> plate = new List<IngredientPortion>();
        /// <summary>Units picked in the kitchen stock list, waiting to be sent somewhere together.</summary>
        private readonly Dictionary<(IngredientDefinition, PrepSteps), int> selection = new Dictionary<(IngredientDefinition, PrepSteps), int>();

        private RectTransform kitchenStockContent;
        private readonly Dictionary<Destination, Button> sendButtons = new Dictionary<Destination, Button>();
        private Text selectionText;
        private Button clearSelectionButton;
        private StationView wokView;
        private StationView potView;
        private GesturePadGraphic wokPad;
        private Text plateText;
        private Button serveButton;
        private Button binPlateButton;
        private Button previewButton;
        private FlavourRadarGraphic plateRadar;

        private void CreateKitchenState()
        {
            wok = CookingStation.CreateWok(wokSeconds, shakeKnockback);
            pot = CookingStation.CreatePot(potSeconds);
            shakeGesture = new BackAndForthGesture(vertical: false, requiredStrokes: int.MaxValue, minTravel: 45f);
        }

        private void TickKitchen(float deltaTime)
        {
            wok.Tick(deltaTime);
            pot.Tick(deltaTime);
            UpdateStationView(wokView);
            UpdateStationView(potView);
        }

        private void ClearKitchen()
        {
            wok.Clear();
            pot.Clear();
            selection.Clear();
            ClearPlate();
        }

        private void ClearPlate()
        {
            plate.Clear();
        }

        // ------------------------------------------------------------------ Actions

        private int Selected(IngredientDefinition ingredient, PrepSteps prep)
        {
            return selection.TryGetValue((ingredient, prep), out int count) ? count : 0;
        }

        private int SelectedTotal
        {
            get
            {
                int total = 0;
                foreach(int count in selection.Values) total += count;
                return total;
            }
        }

        /// <summary>Adds one more unit of <paramref name="entry"/> to the selection, up to what's in stock.</summary>
        private void SelectMore(Inventory.Entry entry)
        {
            if(phase != Phase.Service) return;

            int selected = Selected(entry.Ingredient, entry.Prep);

            if(selected >= inventory.Count(entry.Ingredient, entry.Prep))
            {
                Say("That's all the " + entry.Ingredient.DisplayName + " like that in stock.");
                return;
            }

            selection[(entry.Ingredient, entry.Prep)] = selected + 1;
            RefreshAll();
        }

        private void SelectLess(Inventory.Entry entry)
        {
            int selected = Selected(entry.Ingredient, entry.Prep);
            if(selected <= 1) selection.Remove((entry.Ingredient, entry.Prep));
            else selection[(entry.Ingredient, entry.Prep)] = selected - 1;
            RefreshAll();
        }

        private void ClearSelection()
        {
            selection.Clear();
            RefreshAll();
        }

        /// <summary>Drops selected units that are no longer in stock (prepped or used meanwhile).</summary>
        private void ClampSelection()
        {
            List<(IngredientDefinition, PrepSteps)> keys = new List<(IngredientDefinition, PrepSteps)>(selection.Keys);

            foreach((IngredientDefinition ingredient, PrepSteps prep) key in keys)
            {
                int available = inventory.Count(key.ingredient, key.prep);
                if(available <= 0) selection.Remove(key);
                else if(selection[key] > available) selection[key] = available;
            }
        }

        /// <summary>Moves everything selected to <paramref name="target"/> in one go.</summary>
        private void SendSelection(Destination target)
        {
            if(phase != Phase.Service) return;

            ClampSelection();
            List<IngredientPortion> batch = new List<IngredientPortion>();

            foreach(KeyValuePair<(IngredientDefinition, PrepSteps), int> pair in selection)
            {
                batch.Add(new IngredientPortion(pair.Key.Item1, pair.Value, pair.Key.Item2));
            }

            if(batch.Count == 0)
            {
                Say("Select some stock first: click a row once per unit.", UIFactory.Bad);
                return;
            }

            CookingStation station = target == Destination.Wok ? wok : target == Destination.Pot ? pot : null;

            if(station != null && !station.CanAddAll(batch, out string reason))
            {
                Say(reason, UIFactory.Bad);
                return;
            }

            float cost = 0f;
            int units = 0;

            foreach(IngredientPortion portion in batch)
            {
                inventory.Take(portion.ingredient, portion.prep, portion.quantity);
                cost += portion.ingredient.Price * portion.quantity;
                units += portion.quantity;
            }

            if(station != null)
            {
                station.AddAll(batch);
                Say(units + (units == 1 ? " unit" : " units") + " → " + station.Name + (station.IsSoup ? " (soup)." : ".") + " Press Start when it's all in.");
                LogCooking(station.Name, "Put in " + ListPortions(batch) + (station.IsSoup ? " (soup)." : "."));
            }
            else
            {
                foreach(IngredientPortion portion in batch) AddToPlate(portion);
                Say(units + (units == 1 ? " unit" : " units") + " → plate, raw.");
                LogCooking("Plate", "Raw onto the plate: " + ListPortions(batch) + ".");
            }

            dishCost += cost;
            selection.Clear();
            RefreshAll();
        }

        private void StartStation(StationView view)
        {
            if(view.Station.StartCooking())
            {
                Say(view.Station.Name + " on. Watch the bar.");
                if(view.Station == wok) shakesThisCook = 0;
                LogCooking(view.Station.Name, "Started" + (view.Station.IsSoup ? " the soup." : "."));
            }

            RefreshAll();
        }

        private void TakeOff(StationView view)
        {
            Doneness doneness = view.Station.CurrentDoneness;
            float progress = view.Station.Progress;
            List<IngredientPortion> cooked = view.Station.TakeOff();
            if(cooked.Count == 0) return;

            foreach(IngredientPortion portion in cooked) AddToPlate(portion);

            string shakes = view.Station == wok && shakesThisCook > 0 ? ", after " + shakesThisCook + (shakesThisCook == 1 ? " shake" : " shakes") : "";
            LogCooking(view.Station.Name, "Took it off at " + (progress * 100f).ToString("0") + "%: " + DonenessLabel(doneness) + shakes + ".");

            Say(view.Station.Name + " → plate: " + DonenessLabel(doneness) + ".", doneness == Doneness.Perfect ? UIFactory.Good : UIFactory.Bad);
            RefreshAll();
        }

        private void BinStation(StationView view)
        {
            LogCooking(view.Station.Name, "Binned " + ListPortions(view.Station.Contents) + ".");
            view.Station.Clear();
            Say("Binned the " + view.Station.Name.ToLowerInvariant() + ".");
            RefreshAll();
        }

        private void AddToPlate(IngredientPortion portion)
        {
            foreach(IngredientPortion existing in plate)
            {
                if(existing.ingredient == portion.ingredient && existing.prep == portion.prep
                   && existing.cooking == portion.cooking && (existing.IsRaw || existing.doneness == portion.doneness))
                {
                    existing.quantity += portion.quantity;
                    return;
                }
            }

            plate.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, portion.doneness));
        }

        private void BinPlate()
        {
            LogCooking("Plate", "Binned the plate: " + ListPortions(plate) + ".");
            ClearPlate();
            Say("Binned the plate.");
            RefreshAll();
        }

        private void TogglePreview()
        {
            livePreview = !livePreview;
            RefreshAll();
        }

        private void Serve()
        {
            if(customer == null || !orderIn)
            {
                Say(customer == null ? "Nobody's at the counter." : "Wait for their order.", UIFactory.Bad);
                return;
            }

            if(plate.Count == 0)
            {
                Say("The plate is empty.", UIFactory.Bad);
                return;
            }

            JudgeResult result = DishJudge.Judge(order, plate, customer, zones, rules, JudgeSettings());
            FinishOrder(result);
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

        // ------------------------------------------------------------------ Wok shaking

        private void OnWokPressed(Vector2 position)
        {
            wokPad.AddTrailPoint(position, newStroke: true);
            shakeGesture.Begin(position);
        }

        private void OnWokMoved(Vector2 position)
        {
            wokPad.AddTrailPoint(position);
            if(!shakeGesture.Move(position)) return;

            if(wok.Shake())
            {
                shakesThisCook++;
                Say("Wok magic!", UIFactory.Accent);
            }
            else if(wok.IsCooking && wok.Progress < wok.DangerStart - 0.1f) Say("No need to shake yet. Wait for the red.");
        }

        private void OnWokReleased()
        {
            shakeGesture.End();
        }

        private int shakesThisCook;

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

        // ------------------------------------------------------------------ UI

        private GameObject BuildKitchenArea(RectTransform root)
        {
            BuildTicket(Column(root, "Ticket", 0f, 0.2f));

            RectTransform stock = Column(root, "Stock", 0.2f, 0.37f);
            VerticalLayoutGroup stockLayout = UIFactory.Vertical(stock.gameObject, 8f, 14);
            stockLayout.childForceExpandHeight = false;
            Header(stock, "STOCK  <color=#77736d>click once per unit to select</color>");
            kitchenStockContent = UIFactory.ScrollList(stock);

            selectionText = UIFactory.Label(stock, "", 17);
            UIFactory.Size(selectionText, 24f, 0f);
            RectTransform sendRow = UIFactory.Row(stock, 44f, 6f);
            sendButtons[Destination.Wok] = UIFactory.Button(sendRow, "→ Wok", () => SendSelection(Destination.Wok), new Color(0.75f, 0.42f, 0.15f, 1f), 18);
            sendButtons[Destination.Pot] = UIFactory.Button(sendRow, "→ Pot", () => SendSelection(Destination.Pot), new Color(0.75f, 0.42f, 0.15f, 1f), 18);
            sendButtons[Destination.Plate] = UIFactory.Button(sendRow, "→ Plate", () => SendSelection(Destination.Plate), null, 18);
            RectTransform clearRow = UIFactory.Row(stock, 34f, 6f);
            clearSelectionButton = UIFactory.Button(clearRow, "Clear selection", ClearSelection, null, 16);

            RectTransform stations = Column(root, "Stations", 0.37f, 0.65f, false);
            VerticalLayoutGroup layout = UIFactory.Vertical(stations.gameObject, 10f, 0);
            layout.childForceExpandHeight = false;
            wokView = BuildStation(stations, wok, "WOK  <color=#77736d>take it off in the green, shake in the red</color>");
            potView = BuildStation(stations, pot, "POT  <color=#77736d>take it off on the green line</color>");

            BuildPlateColumn(Column(root, "Plate", 0.65f, 1f));
            return root.gameObject;
        }

        private StationView BuildStation(RectTransform parent, CookingStation station, string title)
        {
            StationView view = new StationView { Station = station };

            RectTransform panel = UIFactory.Panel(parent, station.Name, UIFactory.PanelColor).rectTransform;
            UIFactory.Size(panel, -1f, 1f);
            UIFactory.Vertical(panel.gameObject, 8f, 14);
            Header(panel, title);
            view.Contents = UIFactory.Label(panel, "", 18);
            UIFactory.Size(view.Contents, 48f, 0f);
            view.Bar = new TimingBar(panel, station, 34f);
            view.Status = UIFactory.Label(panel, "", 22, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(view.Status, 28f, 0f);

            if(station.Burns)
            {
                wokPad = BuildPad(panel, "Shake Pad", 80f, 1f, OnWokPressed, OnWokMoved, OnWokReleased);
                wokPad.SetTrailColor(UIFactory.Accent);
                view.PadHint = UIFactory.Label(wokPad.transform, "", 16, TextAnchor.MiddleCenter);
                UIFactory.Stretch(view.PadHint.rectTransform);
            }

            RectTransform row = UIFactory.Row(panel, 46f);
            view.StartButton = UIFactory.Button(row, "Start", () => StartStation(view));
            view.TakeOffButton = UIFactory.Button(row, "Take off → plate", () => TakeOff(view), new Color(0.25f, 0.4f, 0.25f, 1f));
            view.BinButton = UIFactory.Button(row, "Bin", () => BinStation(view), new Color(0.35f, 0.2f, 0.2f, 1f));

            UpdateStationView(view);
            return view;
        }

        private void BuildPlateColumn(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;
            Header(column, "PLATE");
            plateText = UIFactory.Label(column, "", 19);
            UIFactory.Size(plateText, 170f, 0f);

            RectTransform row = UIFactory.Row(column, 52f);
            serveButton = UIFactory.Button(row, "Serve", Serve, new Color(0.75f, 0.42f, 0.15f, 1f), 24);
            binPlateButton = UIFactory.Button(row, "Bin plate", BinPlate, new Color(0.35f, 0.2f, 0.2f, 1f));

            RectTransform radarArea = UIFactory.Rect(column, "Radar");
            UIFactory.Size(radarArea, 300f, 1f);
            plateRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            plateRadar.raycastTarget = false;
            // No zones: just your dish and, when you know it, the order.
            plateRadar.CreateLabels(UIFactory.Font, 16, UIFactory.MutedText);

            Text legend = UIFactory.Label(column, "<color=#d9d9d9>Grey outline</color>: the order.  <color=#f28c33>Orange</color>: your dish so far.",
                16, TextAnchor.UpperLeft, FontStyle.Normal, UIFactory.MutedText);
            UIFactory.Size(legend, 24f, 0f);

            RectTransform previewRow = UIFactory.Row(column, 36f);
            previewButton = UIFactory.Button(previewRow, "", TogglePreview, null, 16);
        }

        private void RefreshKitchen()
        {
            if(plateText == null) return;

            ClampSelection();
            if(area == Area.Kitchen) RebuildKitchenStock();

            int selected = SelectedTotal;
            selectionText.text = selected == 0
                ? "<color=#a6a199>Nothing selected.</color>"
                : "<b>" + selected + (selected == 1 ? " unit" : " units") + "</b> selected. Send them to:";
            foreach(Button button in sendButtons.Values) button.interactable = selected > 0 && phase == Phase.Service;
            sendButtons[Destination.Wok].interactable &= !wok.IsCooking;
            sendButtons[Destination.Pot].interactable &= !pot.IsCooking;
            clearSelectionButton.interactable = selected > 0;

            RefreshStationContents(wokView);
            RefreshStationContents(potView);
            UpdateStationView(wokView);
            UpdateStationView(potView);

            if(plate.Count == 0)
            {
                plateText.text = "<color=#a6a199>Empty.</color>";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach(IngredientPortion portion in plate) sb.Append(Describe(portion, true)).Append('\n');
                sb.Append("<color=#a6a199>Ingredients used: $").Append(dishCost.ToString("0.00")).Append("</color>");
                plateText.text = sb.ToString();
            }

            serveButton.interactable = plate.Count > 0 && orderIn;
            binPlateButton.interactable = plate.Count > 0;
            UIFactory.SetButtonText(previewButton, livePreview ? "Live flavour: on" : "Live flavour: off");

            plateRadar.ClearProfiles();
            // The order only when it's a recipe you know (in the recipe book).
            if(orderIn && order != null && KnownRecipes().Contains(order)) plateRadar.AddProfile(orderProfile, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            if(livePreview && plate.Count > 0) plateRadar.AddProfile(FlavourCalculator.Calculate(plate, rules), UIFactory.Accent, true);
        }

        /// <summary>Kitchen stock rows: click adds a unit to the selection, − takes one back.</summary>
        private void RebuildKitchenStock()
        {
            UIFactory.ClearChildren(kitchenStockContent);
            List<Inventory.Entry> entries = inventory.Entries();

            if(entries.Count == 0)
            {
                Text empty = UIFactory.Label(kitchenStockContent, "<color=#a6a199>Out of stock. The store reopens tomorrow morning.</color>", 17);
                UIFactory.Size(empty, 50f, 0f);
                return;
            }

            Color selectedColor = new Color(0.55f, 0.33f, 0.14f, 1f);

            foreach(Inventory.Entry entry in entries)
            {
                Inventory.Entry captured = entry;
                int selected = Selected(entry.Ingredient, entry.Prep);

                RectTransform row = UIFactory.Row(kitchenStockContent, 38f, 4f);
                HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
                rowLayout.childForceExpandWidth = false;

                string label = Describe(new IngredientPortion(entry.Ingredient, entry.Quantity, entry.Prep), true);
                if(selected > 0) label = "<b>" + selected + "/</b>" + label;
                Color color = selected > 0 ? selectedColor : entry.Prep == PrepSteps.None ? UIFactory.ButtonColor : new Color(0.24f, 0.3f, 0.24f, 1f);

                Button pick = UIFactory.Button(row, label, () => SelectMore(captured), color, 17);
                Text text = pick.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                pick.interactable = phase == Phase.Service;
                UIFactory.Size(pick, 38f, 0f, -1f, 1f);

                Button less = UIFactory.Button(row, "−", () => SelectLess(captured), null, 20);
                less.interactable = selected > 0;
                UIFactory.Size(less, 38f, 0f, 38f, 0f);
            }
        }

        private void RefreshStationContents(StationView view)
        {
            if(view.Station.IsEmpty)
            {
                view.Contents.text = view.Station.OneIngredientWithoutStock
                    ? "<color=#a6a199>Empty. Without a stock cube it boils one ingredient at a time.</color>"
                    : "<color=#a6a199>Empty.</color>";
                return;
            }

            StringBuilder sb = new StringBuilder();
            if(view.Station.IsSoup) sb.Append("<color=#f28c33>Soup:</color> ");
            foreach(IngredientPortion portion in view.Station.Contents) sb.Append(Describe(portion)).Append("   ");
            view.Contents.text = sb.ToString();
        }

        private void UpdateStationView(StationView view)
        {
            CookingStation station = view.Station;
            view.Bar.Set(station.Progress, station.IsCooking);

            if(station.IsCooking)
            {
                Doneness doneness = station.CurrentDoneness;
                string label = DonenessLabel(doneness);
                if(station.Burns && station.IsInDanger && doneness == Doneness.Perfect) label = "perfect, but SHAKE IT!";
                view.Status.text = (station.Progress * 100f).ToString("0") + "%  " + label;
                view.Status.color = doneness == Doneness.Perfect ? (station.IsInDanger ? UIFactory.Accent : UIFactory.Good)
                    : doneness == Doneness.Undercooked ? UIFactory.MutedText : UIFactory.Bad;
            }
            else
            {
                view.Status.text = station.IsEmpty ? "Idle" : "Loaded. Press Start.";
                view.Status.color = UIFactory.MutedText;
            }

            view.StartButton.interactable = !station.IsCooking && !station.IsEmpty;
            view.TakeOffButton.interactable = station.IsCooking;
            view.BinButton.interactable = !station.IsEmpty;

            if(view.PadHint != null)
            {
                bool danger = station.IsInDanger && station.Progress < 1f;

                if(view.WasInDanger != danger)
                {
                    view.WasInDanger = danger;
                    // In the red the pad shows the side-to-side guide; otherwise just how to use it.
                    wokPad.SetGuide(danger ? GestureGuide.SideToSide : GestureGuide.None, UIFactory.Accent);
                    view.PadHint.text = danger ? "" : "Shake pad: when the bar hits red, hold and drag side to side here.";
                    view.PadHint.color = UIFactory.MutedText;
                }
            }
        }
    }
}
