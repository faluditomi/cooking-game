using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Playable cooking loop on top of the flavour system. A customer orders a dish; you buy
    /// ingredients onto the prep board, peel / slice / grate them, cook them in the wok or pot
    /// (timing decides doneness), plate them and serve. The dish is judged with
    /// <see cref="DishJudge"/> and its flavour is revealed on a radar chart.
    /// </summary>
    /// <remarks>The whole UI is built in code at startup, so the scene only needs this component.</remarks>
    public class CookingPrototype : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private FlavourRules rules;
        [SerializeField] private List<FlavourZone> zones = new List<FlavourZone>();
        [SerializeField] private List<IngredientDefinition> pantry = new List<IngredientDefinition>();
        [SerializeField] private List<RecipeDefinition> menu = new List<RecipeDefinition>();
        [SerializeField] private List<Customer> customers = new List<Customer>();

        [Header("Economy")]
        [SerializeField, Min(0f)] private float startingMoney = 60f;
        [Tooltip("Menu price = ingredient cost × margin.")]
        [SerializeField, Min(0f)] private float margin = 1.6f;
        [Tooltip("How well an unordered dish must share a zone with the order to be accepted.")]
        [SerializeField, Range(0f, 1f)] private float likeThreshold = 0.5f;

        [Header("Stations")]
        [SerializeField, Min(1f)] private float wokSeconds = 8f;
        [SerializeField, Min(1f)] private float potSeconds = 10f;

        [Header("Debug")]
        [Tooltip("Show the plate's flavour on the radar while cooking. The design reveals it only after serving; this is for tuning.")]
        [SerializeField] private bool livePreview = true;
        [Tooltip("0 picks a random seed.")]
        [SerializeField] private int randomSeed;

        private class StationView
        {
            public CookingStation Station;
            public Text Contents;
            public Text Status;
            public TimingBar Bar;
            public Button StartButton;
            public Button TakeOffButton;
            public Button ShakeButton;
            public Button BinButton;
        }

        // Game state
        private System.Random random;
        private float money;
        private int served;
        private int happy;
        private float spentOnDish;
        private int customerIndex = -1;
        private Customer customer;
        private RecipeDefinition order;
        private FlavourProfile orderProfile;
        private FlavourZone orderZone;
        private IngredientPortion board;
        private CookingStation wok;
        private CookingStation pot;
        private readonly List<IngredientPortion> plate = new List<IngredientPortion>();
        private bool showingResult;

        // UI
        private Text moneyText;
        private Text servedText;
        private Text messageText;
        private Text customerText;
        private Text orderLineText;
        private Text recipeText;
        private Text boardText;
        private readonly List<Button> boardButtons = new List<Button>();
        private Button sendWokButton;
        private Button sendPotButton;
        private StationView wokView;
        private StationView potView;
        private Text plateText;
        private Button serveButton;
        private Button binPlateButton;
        private Button previewButton;
        private FlavourRadarGraphic plateRadar;
        private GameObject resultOverlay;
        private Text resultTitle;
        private Text resultBody;
        private FlavourRadarGraphic resultRadar;
        private readonly List<KeyValuePair<Button, IngredientDefinition>> pantryButtons = new List<KeyValuePair<Button, IngredientDefinition>>();

        private float Falloff => rules != null ? rules.zoneFalloff : FlavourRules.DefaultZoneFalloff;

        /// <summary>Assigns the data. Used by the editor setup menu.</summary>
        public void Setup(FlavourRules newRules, IEnumerable<FlavourZone> newZones, IEnumerable<IngredientDefinition> newPantry,
            IEnumerable<RecipeDefinition> newMenu, IEnumerable<Customer> newCustomers)
        {
            rules = newRules;
            zones = new List<FlavourZone>(newZones);
            pantry = new List<IngredientDefinition>(newPantry);
            menu = new List<RecipeDefinition>(newMenu);
            customers = new List<Customer>(newCustomers);
        }

        private void Start()
        {
            random = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
            money = startingMoney;
            wok = CookingStation.CreateWok(wokSeconds);
            pot = CookingStation.CreatePot(potSeconds);

            if(customers.Count == 0) customers.Add(new Customer());

            EnsureEventSystem();
            BuildUI();
            NextCustomer();
        }

        private void Update()
        {
            if(showingResult) return;

            wok.Tick(Time.deltaTime);
            pot.Tick(Time.deltaTime);
            UpdateStationView(wokView);
            UpdateStationView(potView);
        }

        // ------------------------------------------------------------------ Actions

        private void BuyIngredient(IngredientDefinition ingredient)
        {
            if(board != null && (board.ingredient != ingredient || board.prep != PrepSteps.None))
            {
                Say("The board is busy. Send " + board.ingredient.DisplayName + " somewhere or bin it first.", UIFactory.Bad);
                return;
            }

            if(money < ingredient.Price)
            {
                Say("Can't afford " + ingredient.DisplayName + ".", UIFactory.Bad);
                return;
            }

            money -= ingredient.Price;
            spentOnDish += ingredient.Price;

            if(board == null) board = new IngredientPortion(ingredient, 1);
            else board.quantity++;

            Say("Bought " + ingredient.DisplayName + " for $" + ingredient.Price.ToString("0.00") + ".");
            Refresh();
        }

        private void Prep(PrepSteps step)
        {
            if(board == null) return;

            if((board.prep & step) != 0)
            {
                Say(board.ingredient.DisplayName + " is already " + step.ToString().ToLowerInvariant() + ".");
                return;
            }

            board.prep |= step;
            Say(step + " " + board.ingredient.DisplayName + ".");
            Refresh();
        }

        private void SendToStation(CookingStation station)
        {
            if(board == null) return;

            if(!station.Add(board))
            {
                Say("The " + station.Name.ToLowerInvariant() + " is cooking. Wait or take it off first.", UIFactory.Bad);
                return;
            }

            Say(Describe(board) + " → " + station.Name + ".");
            board = null;
            Refresh();
        }

        private void SendToPlateRaw()
        {
            if(board == null) return;

            AddToPlate(board);
            Say(Describe(board) + " → plate, raw.");
            board = null;
            Refresh();
        }

        private void BinBoard()
        {
            if(board == null) return;
            Say("Binned " + Describe(board) + ".");
            board = null;
            Refresh();
        }

        private void StartStation(StationView view)
        {
            if(view.Station.StartCooking()) Say(view.Station.Name + " on. Watch the bar.");
            Refresh();
        }

        private void TakeOff(StationView view)
        {
            Doneness doneness = view.Station.CurrentDoneness;
            List<IngredientPortion> cooked = view.Station.TakeOff();
            if(cooked.Count == 0) return;

            foreach(IngredientPortion portion in cooked) AddToPlate(portion);

            Say(view.Station.Name + " → plate: " + DonenessLabel(doneness) + ".", doneness == Doneness.Perfect ? UIFactory.Good : UIFactory.Bad);
            Refresh();
        }

        private void Shake(StationView view)
        {
            if(view.Station.Shake()) Say("Wok magic.");
        }

        private void BinStation(StationView view)
        {
            view.Station.Clear();
            Say("Binned the " + view.Station.Name.ToLowerInvariant() + ".");
            Refresh();
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
            plate.Clear();
            Say("Binned the plate.");
            Refresh();
        }

        private void TogglePreview()
        {
            livePreview = !livePreview;
            Refresh();
        }

        private void Serve()
        {
            if(plate.Count == 0)
            {
                Say("The plate is empty.", UIFactory.Bad);
                return;
            }

            DishJudge.Settings settings = DishJudge.Settings.Default;
            settings.Margin = margin;
            settings.LikeThreshold = likeThreshold;

            JudgeResult result = DishJudge.Judge(order, plate, customer, zones, rules, settings);
            money += result.Payment + result.Tip;
            served++;
            if(result.IsPaid) happy++;

            ShowResult(result);
            Refresh();
        }

        private void NextCustomer()
        {
            showingResult = false;
            resultOverlay.SetActive(false);
            plate.Clear();
            spentOnDish = 0f;

            if(customers.Count > 1)
            {
                int next;
                do next = random.Next(customers.Count); while(next == customerIndex);
                customerIndex = next;
            }
            else
            {
                customerIndex = 0;
            }

            customer = customers[customerIndex];
            List<RecipeDefinition> options = customer.orders != null && customer.orders.Count > 0 ? customer.orders : menu;
            order = options.Count > 0 ? options[random.Next(options.Count)] : null;
            orderProfile = order != null ? order.CalculateProfile(rules) : FlavourProfile.Zero;
            orderZone = FlavourEvaluator.FindBestZone(orderProfile, zones, Falloff).Zone;

            customerText.text = customer.name;
            orderLineText.text = "\"" + customer.GetOrderLine(order, random) + "\"";
            recipeText.text = DescribeRecipe(order);

            Say(customer.name + " is waiting.");
            Refresh();
        }

        // ------------------------------------------------------------------ Text

        private static string DonenessLabel(Doneness doneness)
        {
            switch(doneness)
            {
                case Doneness.Perfect: return "perfect";
                case Doneness.Undercooked: return "undercooked";
                case Doneness.Overcooked: return "overcooked";
                case Doneness.Burnt: return "burnt";
                default: return doneness.ToString();
            }
        }

        private static string Describe(IngredientPortion portion, bool markRaw = false)
        {
            List<string> parts = new List<string>();
            if((portion.prep & PrepSteps.Peeled) != 0) parts.Add("peeled");
            if((portion.prep & PrepSteps.Sliced) != 0) parts.Add("sliced");
            if((portion.prep & PrepSteps.Grated) != 0) parts.Add("grated");
            if((portion.cooking & CookMethods.Boiled) != 0) parts.Add("boiled");
            if((portion.cooking & CookMethods.Fried) != 0) parts.Add("fried");

            if(portion.IsRaw)
            {
                if(markRaw) parts.Add("raw");
            }
            else if(portion.doneness != Doneness.Perfect)
            {
                parts.Add("<color=#e5594d>" + DonenessLabel(portion.doneness) + "</color>");
            }

            string text = portion.quantity + "× " + portion.ingredient.DisplayName;
            return parts.Count > 0 ? text + "  <color=#a6a199>" + string.Join(", ", parts) + "</color>" : text;
        }

        private string DescribeRecipe(RecipeDefinition recipe)
        {
            if(recipe == null) return "<color=#a6a199>Nothing on the menu.</color>";

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=24><b>").Append(recipe.DisplayName).Append("</b></size>\n");
            if(!string.IsNullOrEmpty(recipe.Origin)) sb.Append("<color=#a6a199>Origin: ").Append(recipe.Origin).Append("</color>\n");
            sb.Append('\n');

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion != null && portion.IsValid) sb.Append("• ").Append(Describe(portion, true)).Append('\n');
            }

            float cost = recipe.CalculateIngredientCost();
            sb.Append("\nMenu price <b>$").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00"))
              .Append("</b>  <color=#a6a199>(ingredients $").Append(cost.ToString("0.00")).Append(")</color>");

            if(orderZone != null)
            {
                sb.Append("\nTastes like <color=#").Append(ColorUtility.ToHtmlStringRGB(orderZone.Color)).Append(">")
                  .Append(orderZone.DisplayName).Append("</color>");
            }
            else
            {
                sb.Append("\n<color=#a6a199>Not in any desirable zone.</color>");
            }

            return sb.ToString();
        }

        private void Say(string text, Color? color = null)
        {
            if(messageText == null) return;
            messageText.text = text;
            messageText.color = color ?? UIFactory.TextColor;
        }

        // ------------------------------------------------------------------ Refresh

        private void Refresh()
        {
            moneyText.text = "$" + money.ToString("0.00");
            servedText.text = "Served " + served + "   ·   Happy " + happy;

            boardText.text = board == null
                ? "<color=#a6a199>Empty. Buy something from the pantry. Click it again for more.</color>"
                : "<size=22>" + Describe(board, false) + "</size>";
            foreach(Button button in boardButtons) button.interactable = board != null;
            sendWokButton.interactable = board != null && !wok.IsCooking;
            sendPotButton.interactable = board != null && !pot.IsCooking;

            foreach(KeyValuePair<Button, IngredientDefinition> pair in pantryButtons)
            {
                pair.Key.interactable = money >= pair.Value.Price;
            }

            RefreshStationContents(wokView);
            RefreshStationContents(potView);

            if(plate.Count == 0)
            {
                plateText.text = "<color=#a6a199>Empty.</color>";
            }
            else
            {
                StringBuilder sb = new StringBuilder();
                foreach(IngredientPortion portion in plate) sb.Append(Describe(portion, true)).Append('\n');
                sb.Append("<color=#a6a199>Spent on this dish: $").Append(spentOnDish.ToString("0.00")).Append("</color>");
                plateText.text = sb.ToString();
            }

            serveButton.interactable = plate.Count > 0;
            binPlateButton.interactable = plate.Count > 0;
            UIFactory.SetButtonText(previewButton, livePreview ? "Live flavour: on" : "Live flavour: off");

            plateRadar.SetHighlight(orderZone);
            plateRadar.ClearProfiles();
            plateRadar.AddProfile(orderProfile, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            if(livePreview && plate.Count > 0) plateRadar.AddProfile(FlavourCalculator.Calculate(plate, rules), UIFactory.Accent, true);
        }

        private void RefreshStationContents(StationView view)
        {
            if(view.Station.IsEmpty)
            {
                view.Contents.text = "<color=#a6a199>Empty.</color>";
                return;
            }

            StringBuilder sb = new StringBuilder();
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
            if(view.ShakeButton != null) view.ShakeButton.interactable = station.IsInDanger && station.Progress < 1f;
            view.BinButton.interactable = !station.IsEmpty;
        }

        private void ShowResult(JudgeResult result)
        {
            showingResult = true;
            resultOverlay.SetActive(true);

            string title;
            string line;
            Color color;

            switch(result.Verdict)
            {
                case Verdict.DreamMeal:
                    title = "Dream meal!"; line = customer.dreamLine; color = UIFactory.Accent; break;
                case Verdict.Ordered:
                    title = "Just what they ordered"; line = customer.likedLine; color = UIFactory.Good; break;
                case Verdict.LikedSubstitute:
                    title = "Not what they ordered, but they like it"; line = customer.likedLine; color = UIFactory.Good; break;
                case Verdict.Refused:
                    title = "Refused"; line = customer.refusedLine; color = UIFactory.Bad; break;
                default:
                    title = "They didn't like it"; line = customer.dislikedLine; color = UIFactory.Bad; break;
            }

            resultTitle.text = title + "\n<size=22><i>" + customer.name + ": \"" + line + "\"</i></size>";
            resultTitle.color = color;

            StringBuilder sb = new StringBuilder();
            sb.Append("<b>Ordered</b>  ").Append(order != null ? order.DisplayName : "-").Append('\n');
            sb.Append("<b>Served</b>\n");
            foreach(IngredientPortion portion in plate) sb.Append("  ").Append(Describe(portion, true)).Append('\n');

            sb.Append("\n<b>Flavour</b>\n<size=17>").Append(result.Dish.ToString()).Append("</size>\n\n");
            sb.Append("Exact order: ").Append(result.ExactMatch ? "<color=#73cc73>yes</color>" : "no").Append('\n');
            sb.Append("Best zone: ").Append(ZoneText(result.BestZone)).Append('\n');
            sb.Append("Same zone as the order: ").Append(ZoneText(result.SharedZone)).Append('\n');
            if(result.RefusedIngredient != null) sb.Append("Won't eat: <color=#e5594d>").Append(result.RefusedIngredient.DisplayName).Append("</color>\n");
            if(result.DesireScore >= 0f) sb.Append("<color=#a6a199>Secret desire (debug): ").Append((result.DesireScore * 100f).ToString("0")).Append("%</color>\n");

            float profit = result.Payment + result.Tip - spentOnDish;
            sb.Append("\nPaid <b>$").Append(result.Payment.ToString("0.00")).Append("</b>");
            if(result.Tip > 0f) sb.Append("  + tip <b>$").Append(result.Tip.ToString("0.00")).Append("</b>");
            sb.Append("\nSpent on ingredients $").Append(spentOnDish.ToString("0.00"));
            sb.Append("\nProfit <b><color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color></b>");
            resultBody.text = sb.ToString();

            FlavourZone highlight = result.SharedZone.Zone != null ? result.SharedZone.Zone : result.BestZone.Zone;
            resultRadar.SetHighlight(highlight);
            resultRadar.ClearProfiles();
            resultRadar.AddProfile(result.Ordered, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            resultRadar.AddProfile(result.Dish, UIFactory.Accent, true);
        }

        private static string ZoneText(FlavourZoneMatch match)
        {
            if(!match.IsMatch) return "<color=#a6a199>none</color>";
            return "<color=#" + ColorUtility.ToHtmlStringRGB(match.Zone.Color) + ">" + match.Zone.DisplayName + "</color> "
                   + (match.Score * 100f).ToString("0") + "%";
        }

        // ------------------------------------------------------------------ UI construction

        private static void EnsureEventSystem()
        {
            if(FindAnyObjectByType<EventSystem>() != null) return;

            GameObject go = new GameObject("EventSystem", typeof(EventSystem));
            go.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("Prototype UI", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            Transform root = canvasObject.transform;

            UIFactory.Stretch(UIFactory.Panel(root, "Background", UIFactory.Background).rectTransform);

            // Top bar
            RectTransform top = UIFactory.Panel(root, "Top Bar", UIFactory.PanelDark).rectTransform;
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(0f, -60f);
            top.offsetMax = Vector2.zero;
            Text title = UIFactory.Label(top, "Emphis's Stall  <size=18><color=#a6a199>cooking prototype</color></size>", 28, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Place(title.rectTransform, 0f, 0f, 0.4f, 1f).offsetMin = new Vector2(20f, 0f);
            servedText = UIFactory.Label(top, "", 22, TextAnchor.MiddleCenter);
            UIFactory.Place(servedText.rectTransform, 0.4f, 0f, 0.7f, 1f);
            moneyText = UIFactory.Label(top, "", 30, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);
            UIFactory.Place(moneyText.rectTransform, 0.7f, 0f, 1f, 1f).offsetMax = new Vector2(-20f, 0f);

            // Message bar
            RectTransform bottom = UIFactory.Panel(root, "Message Bar", UIFactory.PanelDark).rectTransform;
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0f, 44f);
            messageText = UIFactory.Label(bottom, "", 20, TextAnchor.MiddleLeft);
            UIFactory.Stretch(messageText.rectTransform).offsetMin = new Vector2(20f, 0f);

            RectTransform content = UIFactory.Rect(root, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(0f, 44f);
            content.offsetMax = new Vector2(0f, -60f);

            BuildOrderColumn(Column(content, "Order", 0f, 0.2f));
            BuildPantryColumn(Column(content, "Pantry", 0.2f, 0.37f));
            BuildKitchenColumn(Column(content, "Kitchen", 0.37f, 0.65f, false));
            BuildPlateColumn(Column(content, "Plate", 0.65f, 1f));
            BuildResultOverlay(root);
        }

        private static RectTransform Column(RectTransform parent, string name, float xMin, float xMax, bool panel = true)
        {
            RectTransform column = panel
                ? UIFactory.Panel(parent, name, UIFactory.PanelColor).rectTransform
                : UIFactory.Rect(parent, name);
            UIFactory.Place(column, xMin, 0f, xMax, 1f, 6f);
            return column;
        }

        private static Text Header(Transform parent, string text)
        {
            Text header = UIFactory.Label(parent, text, 16, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.MutedText);
            UIFactory.Size(header, 22f);
            return header;
        }

        private void BuildOrderColumn(RectTransform column)
        {
            UIFactory.Vertical(column.gameObject, 10f, 16);
            Header(column, "CUSTOMER");
            customerText = UIFactory.Label(column, "", 30, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            orderLineText = UIFactory.Label(column, "", 20, TextAnchor.UpperLeft, FontStyle.Italic);
            UIFactory.Size(UIFactory.Rect(column, "Spacer"), 10f);
            Header(column, "ORDER");
            recipeText = UIFactory.Label(column, "", 19);
        }

        private void BuildPantryColumn(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;
            Header(column, "PANTRY  <color=#77736d>click to buy one</color>");

            RectTransform grid = UIFactory.Rect(column, "Grid");
            GridLayoutGroup gridLayout = grid.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(140f, 56f);
            gridLayout.spacing = new Vector2(8f, 8f);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 2;
            UIFactory.Size(grid, -1f, 1f);

            foreach(IngredientDefinition ingredient in pantry)
            {
                if(ingredient == null) continue;
                IngredientDefinition captured = ingredient;
                Button button = UIFactory.Button(grid, ingredient.DisplayName + "\n<size=14><color=#a6a199>$" + ingredient.Price.ToString("0.00") + "</color></size>",
                    () => BuyIngredient(captured), null, 17);
                pantryButtons.Add(new KeyValuePair<Button, IngredientDefinition>(button, ingredient));
            }
        }

        private void BuildKitchenColumn(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 12f, 0);
            layout.childForceExpandHeight = true;

            // Prep board
            RectTransform boardPanel = UIFactory.Panel(column, "Prep Board", UIFactory.PanelColor).rectTransform;
            UIFactory.Vertical(boardPanel.gameObject, 8f, 14);
            Header(boardPanel, "PREP BOARD");
            boardText = UIFactory.Label(boardPanel, "", 20);
            UIFactory.Size(boardText, 54f);

            RectTransform prepRow = UIFactory.Row(boardPanel, 44f);
            boardButtons.Add(UIFactory.Button(prepRow, "Peel", () => Prep(PrepSteps.Peeled)));
            boardButtons.Add(UIFactory.Button(prepRow, "Slice", () => Prep(PrepSteps.Sliced)));
            boardButtons.Add(UIFactory.Button(prepRow, "Grate", () => Prep(PrepSteps.Grated)));

            RectTransform sendRow = UIFactory.Row(boardPanel, 44f);
            sendWokButton = UIFactory.Button(sendRow, "→ Wok", () => SendToStation(wok));
            sendPotButton = UIFactory.Button(sendRow, "→ Pot", () => SendToStation(pot));
            boardButtons.Add(UIFactory.Button(sendRow, "→ Plate (raw)", SendToPlateRaw));
            boardButtons.Add(UIFactory.Button(sendRow, "Bin", BinBoard, new Color(0.35f, 0.2f, 0.2f, 1f)));

            wokView = BuildStation(column, wok, "WOK  <color=#77736d>frying: take it off in the green, shake in the red</color>");
            potView = BuildStation(column, pot, "POT  <color=#77736d>boiling: take it off on the green line</color>");
        }

        private StationView BuildStation(RectTransform parent, CookingStation station, string title)
        {
            StationView view = new StationView { Station = station };

            RectTransform panel = UIFactory.Panel(parent, station.Name, UIFactory.PanelColor).rectTransform;
            UIFactory.Vertical(panel.gameObject, 8f, 14);
            Header(panel, title);
            view.Contents = UIFactory.Label(panel, "", 18);
            UIFactory.Size(view.Contents, 48f);
            view.Bar = new TimingBar(panel, station);
            view.Status = UIFactory.Label(panel, "", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(view.Status, 26f);

            RectTransform row = UIFactory.Row(panel, 44f);
            view.StartButton = UIFactory.Button(row, "Start", () => StartStation(view));
            view.TakeOffButton = UIFactory.Button(row, "Take off → plate", () => TakeOff(view), new Color(0.25f, 0.4f, 0.25f, 1f));
            if(station.Burns) view.ShakeButton = UIFactory.Button(row, "Shake!", () => Shake(view), new Color(0.55f, 0.3f, 0.12f, 1f));
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
            UIFactory.Size(plateText, 190f);

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
            UIFactory.Size(legend, 44f);

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

        private void BuildResultOverlay(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Result Overlay", new Color(0f, 0f, 0f, 0.78f));
            UIFactory.Stretch(overlay.rectTransform);
            resultOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Result", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1400f, 820f);

            RectTransform radarArea = UIFactory.Rect(panel, "Radar");
            UIFactory.Place(radarArea, 0f, 0f, 0.5f, 1f, 30f);
            resultRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            resultRadar.raycastTarget = false;
            resultRadar.SetZones(zones);
            resultRadar.CreateLabels(UIFactory.Font, 18, UIFactory.MutedText);

            RectTransform right = UIFactory.Rect(panel, "Details");
            UIFactory.Place(right, 0.5f, 0f, 1f, 1f, 30f);
            VerticalLayoutGroup layout = UIFactory.Vertical(right.gameObject, 14f, 0);
            layout.childForceExpandHeight = false;

            resultTitle = UIFactory.Label(right, "", 36, TextAnchor.UpperLeft, FontStyle.Bold);
            resultBody = UIFactory.Label(right, "", 20);
            UIFactory.Size(resultBody, -1f, 1f);
            RectTransform row = UIFactory.Row(right, 56f);
            UIFactory.Button(row, "Next customer", NextCustomer, new Color(0.75f, 0.42f, 0.15f, 1f), 24);

            resultOverlay.SetActive(false);
        }
    }
}
