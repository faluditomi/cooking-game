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
        private Destination destination = Destination.Wok;

        private RectTransform kitchenStockContent;
        private readonly Dictionary<Destination, Button> destinationButtons = new Dictionary<Destination, Button>();
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
            ClearPlate();
        }

        private void ClearPlate()
        {
            plate.Clear();
        }

        // ------------------------------------------------------------------ Actions

        private void SetDestination(Destination target)
        {
            destination = target;
            RefreshAll();
        }

        /// <summary>Moves one unit of <paramref name="entry"/> from stock to the chosen destination.</summary>
        private void UseStock(Inventory.Entry entry)
        {
            if(phase != Phase.Service) return;

            IngredientPortion unit = new IngredientPortion(entry.Ingredient, 1, entry.Prep);

            if(destination != Destination.Plate)
            {
                CookingStation station = destination == Destination.Wok ? wok : pot;

                if(!station.CanAdd(unit, out string reason))
                {
                    Say(reason, UIFactory.Bad);
                    return;
                }

                if(!inventory.Take(entry.Ingredient, entry.Prep)) return;
                station.Add(unit);
                Say(Describe(unit, true) + " → " + station.Name + (station.IsSoup ? " (soup)." : "."));
            }
            else
            {
                if(!inventory.Take(entry.Ingredient, entry.Prep)) return;
                AddToPlate(unit);
                Say(Describe(unit, true) + " → plate, raw.");
            }

            dishCost += entry.Ingredient.Price;
            RefreshAll();
        }

        private void StartStation(StationView view)
        {
            if(view.Station.StartCooking()) Say(view.Station.Name + " on. Watch the bar.");
            RefreshAll();
        }

        private void TakeOff(StationView view)
        {
            Doneness doneness = view.Station.CurrentDoneness;
            List<IngredientPortion> cooked = view.Station.TakeOff();
            if(cooked.Count == 0) return;

            foreach(IngredientPortion portion in cooked) AddToPlate(portion);

            Say(view.Station.Name + " → plate: " + DonenessLabel(doneness) + ".", doneness == Doneness.Perfect ? UIFactory.Good : UIFactory.Bad);
            RefreshAll();
        }

        private void BinStation(StationView view)
        {
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
            if(!orderTaken)
            {
                Say("Take the order first.", UIFactory.Bad);
                return;
            }

            if(plate.Count == 0)
            {
                Say("The plate is empty.", UIFactory.Bad);
                return;
            }

            DishJudge.Settings settings = DishJudge.Settings.Default;
            settings.Margin = margin;
            settings.LikeThreshold = likeThreshold;

            JudgeResult result = DishJudge.Judge(order, plate, customer, zones, rules, settings, late: orderTimeLeft < 0f);
            FinishOrder(result);
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

            if(wok.Shake()) Say("Wok magic!", UIFactory.Accent);
            else if(wok.IsCooking && wok.Progress < wok.DangerStart - 0.1f) Say("No need to shake yet. Wait for the red.");
        }

        private void OnWokReleased()
        {
            shakeGesture.End();
        }

        // ------------------------------------------------------------------ UI

        private GameObject BuildKitchenArea(RectTransform root)
        {
            BuildTicket(Column(root, "Ticket", 0f, 0.2f));

            RectTransform stock = Column(root, "Stock", 0.2f, 0.37f);
            VerticalLayoutGroup stockLayout = UIFactory.Vertical(stock.gameObject, 8f, 14);
            stockLayout.childForceExpandHeight = false;
            Header(stock, "STOCK  <color=#77736d>click to move one</color>");
            RectTransform destinationRow = UIFactory.Row(stock, 40f, 6f);
            destinationButtons[Destination.Wok] = UIFactory.Button(destinationRow, "→ Wok", () => SetDestination(Destination.Wok), null, 17);
            destinationButtons[Destination.Pot] = UIFactory.Button(destinationRow, "→ Pot", () => SetDestination(Destination.Pot), null, 17);
            destinationButtons[Destination.Plate] = UIFactory.Button(destinationRow, "→ Plate", () => SetDestination(Destination.Plate), null, 17);
            kitchenStockContent = UIFactory.ScrollList(stock);

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
            plateRadar.SetZones(zones);
            plateRadar.CreateLabels(UIFactory.Font, 16, UIFactory.MutedText);

            Text legend = UIFactory.Label(column, LegendText(), 16, TextAnchor.UpperLeft, FontStyle.Normal, UIFactory.MutedText);
            UIFactory.Size(legend, 44f, 0f);

            RectTransform previewRow = UIFactory.Row(column, 36f);
            previewButton = UIFactory.Button(previewRow, "", TogglePreview, null, 16);
        }

        private string LegendText()
        {
            StringBuilder sb = new StringBuilder("<color=#d9d9d9>Grey outline</color>: what they ordered.  <color=#f28c33>Orange</color>: your plate.\n");

            foreach(FlavourZone zone in zones)
            {
                if(zone == null) continue;
                sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(zone.Color)).Append(">■ ").Append(zone.DisplayName).Append("</color>   ");
            }

            return sb.ToString();
        }

        private void RefreshKitchen()
        {
            if(plateText == null) return;

            if(area == Area.Kitchen) RebuildStockList(kitchenStockContent, UseStock, phase == Phase.Service);

            foreach(KeyValuePair<Destination, Button> pair in destinationButtons)
            {
                pair.Value.GetComponent<Image>().color = destination == pair.Key ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor;
            }

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

            serveButton.interactable = plate.Count > 0 && orderTaken;
            binPlateButton.interactable = plate.Count > 0;
            UIFactory.SetButtonText(previewButton, livePreview ? "Live flavour: on" : "Live flavour: off");

            plateRadar.SetHighlight(orderTaken ? orderZone : null);
            plateRadar.ClearProfiles();
            if(orderTaken) plateRadar.AddProfile(orderProfile, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            if(livePreview && plate.Count > 0) plateRadar.AddProfile(FlavourCalculator.Calculate(plate, rules), UIFactory.Accent, true);
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
