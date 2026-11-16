using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Playable cycle loop on top of the flavour system.
    /// <list type="number">
    /// <item><b>Morning</b>: fill the cart in the <b>Store</b>. Nothing is timed. "Buy ingredients
    /// and start the day" pays for the cart, starts the timer and opens the stall.</item>
    /// <item><b>Service</b>: the store closes and the day timer runs. Every regular comes once, in a
    /// shuffled order, a few seconds apart. They order as they arrive (their order dialogue names the
    /// dish) and the ticket fills in. Prep on the <b>Prep</b> board while they come, and cook in the
    /// <b>Kitchen</b>.</item>
    /// <item><b>After each order</b>: their reaction, the dish's flavour profile, and, for a dish
    /// that isn't on the menu, a chance to name it and save it to the menu.</item>
    /// <item><b>End of the cycle</b>: when everyone has been or the timer runs out. A summary of
    /// dishes, income and expenses, then the stall rent. If you can't pay it, it's game over.
    /// Prepped leftovers spoil; raw stock keeps.</item>
    /// </list>
    /// The whole UI is built in code at startup, so the scene only needs this component. The areas
    /// and other parts live in the partial files CookingPrototype.*.cs.
    /// </summary>
    public partial class CookingPrototype : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private FlavourRules rules;
        [SerializeField] private List<FlavourZone> zones = new List<FlavourZone>();
        [SerializeField] private List<IngredientDefinition> pantry = new List<IngredientDefinition>();
        [SerializeField] private List<RecipeDefinition> menu = new List<RecipeDefinition>();
        [SerializeField] private List<Customer> customers = new List<Customer>();

        [Header("Cycle")]
        [SerializeField, Min(0f)] private float startingMoney = 60f;
        [Tooltip("Paid at the end of every cycle. Can't pay it: game over.")]
        [SerializeField, Min(0f)] private float stallRent = 10f;
        [Tooltip("Off: the day has no timer and ends when everyone has been (the calmer version). Can also be switched in game each morning.")]
        [SerializeField] private bool dayTimer = true;
        [SerializeField, Min(30f)] private float daySeconds = 360f;
        [Tooltip("Seconds between one customer leaving and the next arriving (random in this range).")]
        [SerializeField] private Vector2 arrivalDelay = new Vector2(1f, 6f);

        [Header("Orders")]
        [Tooltip("Menu price = ingredient cost × margin.")]
        [SerializeField, Min(0f)] private float margin = 1.6f;
        [Header("Good enough (how close a dish must be to the order)")]
        [Tooltip("Share of similarity that comes from flavour (radar distance); the rest comes from ingredients, prep and cooking.")]
        [SerializeField, Range(0f, 1f)] private float flavourWeight = 0.5f;
        [Tooltip("Radar distance at which flavour similarity drops to 0.")]
        [SerializeField, Min(0.5f)] private float flavourRange = 6f;
        [SerializeField, Range(0f, 1f)] private float veryCloseAt = 0.8f;
        [Tooltip("Share of the price paid for a very close dish.")]
        [SerializeField, Range(0f, 1f)] private float veryClosePay = 0.8f;
        [Tooltip("Below this similarity they refuse to pay.")]
        [SerializeField, Range(0f, 1f)] private float closeEnoughAt = 0.55f;
        [Tooltip("Share of the price paid for a close-enough dish.")]
        [SerializeField, Range(0f, 1f)] private float closeEnoughPay = 0.6f;

        [Header("Stations")]
        [Tooltip("Seconds for the wok bar to go from empty to the end. Each counted shake sends it back to the middle of the grey.")]
        [SerializeField, Min(1f)] private float wokSeconds = 8f;
        [SerializeField, Min(1f)] private float potSeconds = 10f;

        [Header("Prep minigames (strokes per unit)")]
        [SerializeField, Min(1)] private int sliceStrokes = 6;
        [SerializeField, Min(1)] private int peelStrokes = 3;
        [SerializeField, Min(1)] private int grateStrokes = 5;

        [Header("Debug")]
        [Tooltip("Show the plate's flavour on the radar while cooking. The design reveals it only after serving; this is for tuning.")]
        [SerializeField] private bool livePreview = true;
        [Tooltip("Show how close a dish came to the customer's secret desire on the result screen. Tuning only; it gives the secret away.")]
        [SerializeField] private bool showDesireDebug;
        [Tooltip("0 picks a random seed.")]
        [SerializeField] private int randomSeed;

        /// <summary>Share of the screen width the speech sidebar takes on the right.</summary>
        private const float SidebarWidth = 0.2f;

        private enum Phase
        {
            Morning,
            Service,
            DayOver,
        }

        private enum Area
        {
            Store,
            Prep,
            Kitchen,
        }

        /// <summary>The order ticket, shown on both the Prep and Kitchen areas.</summary>
        private class TicketView
        {
            public Text Customer;
            public Text Line;
            public Text Order;
            public Text Timer;
            public GameObject TimerBar;
            public RectTransform TimerFill;
            public Image TimerFillImage;
        }

        // Cycle state
        private System.Random random;
        private float money;
        private int day = 1;
        private Phase phase;
        private Area area;
        private readonly Inventory inventory = new Inventory();
        private bool overlayOpen;
        private bool gameOver;
        private int servedToday;
        private int likedToday;
        private float spentToday;
        private float earnedToday;
        private float dayTimeLeft;
        /// <summary>Today's regulars still to come, in arrival order.</summary>
        private readonly List<Customer> arrivals = new List<Customer>();
        /// <summary>When the next customer arrives (Time.time), while the counter is empty.</summary>
        private float nextArrivalAt;
        /// <summary>The menu as the scene set it up, for restarting.</summary>
        private List<RecipeDefinition> startingMenu;
        /// <summary>Dishes taken off the menu (soft delete): still known, not shown or named.</summary>
        private readonly HashSet<RecipeDefinition> deletedDishes = new HashSet<RecipeDefinition>();

        // Current customer
        private Customer customer;
        private RecipeDefinition order;
        private FlavourProfile orderProfile;
        /// <summary>The order is a recipe in your book, so you may see what's in it.</summary>
        private bool orderKnown;
        private string orderLine;
        /// <summary>True once the customer has said what they want.</summary>
        private bool orderIn;
        /// <summary>Ingredient cost of what went into the current dish.</summary>
        private float dishCost;

        // Shared UI
        private Text dayText;
        private Text moneyText;
        private Text messageText;
        private Button phaseButton;
        private readonly Dictionary<Area, Button> tabs = new Dictionary<Area, Button>();
        private readonly Dictionary<Area, GameObject> areaRoots = new Dictionary<Area, GameObject>();
        private readonly List<TicketView> tickets = new List<TicketView>();
        private GameObject resultOverlay;
        private Text resultTitle;
        private Text resultBody;
        private RectTransform resultScroll;
        private FlavourRadarGraphic resultRadar;
        private Button resultButton;
        private GameObject summaryOverlay;
        private Text summaryTitle;
        private Text summaryBody;
        private Button summaryButton;

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
#if UNITY_STANDALONE && !UNITY_EDITOR
            // Always full screen. Unity remembers the last window mode per machine, so one windowed
            // launch (say with -screen-fullscreen 0) would otherwise stick.
            Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
#endif
            random = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
            money = startingMoney;
            startingMenu = new List<RecipeDefinition>(menu);
            if(customers.Count == 0) customers.Add(new Customer());

            // Secret desires are undiscovered recipes: never drawn, listed or named as a zone.
            zones.RemoveAll(zone => zone == null || customers.Exists(c => c != null && c.secretDesire == zone));

            CreateKitchenState();
            EnsureEventSystem();
            BuildUI();
            InitNpcs();
            StartDay();
            ShowTitle();
        }

        private void Update()
        {
            // Lines keep typing out on the result screen too.
            TickSpeech(Time.deltaTime);

            if(overlayOpen || phase != Phase.Service) return;

            // The kitchen keeps cooking and the clock keeps running whichever area you're in.
            TickKitchen(Time.deltaTime);

            if(dayTimer)
            {
                dayTimeLeft -= Time.deltaTime;

                if(dayTimeLeft <= 0f)
                {
                    dayTimeLeft = 0f;
                    EndDay();
                    return;
                }
            }

            if(customer == null && Time.time >= nextArrivalAt) NextCustomer();
            else if(customer != null) TickVisit();

            UpdateTicketTimers();
        }

        // ------------------------------------------------------------------ Cycle flow

        private void StartDay()
        {
            phase = Phase.Morning;
            servedToday = 0;
            likedToday = 0;
            spentToday = 0f;
            earnedToday = 0f;
            dayTimeLeft = daySeconds;
            progressToday.Clear();
            customer = null;
            order = null;
            orderIn = false;
            overlayOpen = false;
            summaryOverlay.SetActive(false);
            resultOverlay.SetActive(false);
            ClearSpeech();

            // Everyone comes once a cycle, in a new order each time.
            arrivals.Clear();
            arrivals.AddRange(customers);
            for(int i = arrivals.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (arrivals[i], arrivals[j]) = (arrivals[j], arrivals[i]);
            }

            ShowArea(Area.Store);
            Say("Cycle " + day + ". Fill the cart for " + customers.Count + " regulars, then buy and start the day. Prepping starts with the day.");
        }

        /// <summary>The morning button: pays for the cart and opens the stall.</summary>
        private void OpenStall()
        {
            if(phase != Phase.Morning) return;

            if(!cart.IsEmpty)
            {
                float total = cart.TotalCost;
                int units = cart.TotalUnits;

                if(!cart.Checkout(inventory, ref money))
                {
                    Say("You can't afford the cart: $" + total.ToString("0.00") + ", and you have $" + money.ToString("0.00") + ". Take something out first.", UIFactory.Bad);
                    ShowArea(Area.Store);
                    return;
                }

                spentToday += total;
                Say("Bought " + units + " units for $" + total.ToString("0.00") + ".", UIFactory.Good);
            }

            if(inventory.IsEmpty && boardUnit == null)
            {
                Say("You have nothing to cook with. Put something in the cart first.", UIFactory.Bad);
                ShowArea(Area.Store);
                return;
            }

            phase = Phase.Service;
            if(recipeBookOverlay != null) recipeBookOverlay.SetActive(false);
            ShowArea(Area.Kitchen);
            ScheduleArrival();
        }

        /// <summary>Empties the counter; the next customer comes a few seconds later, or the day ends.</summary>
        private void ScheduleArrival()
        {
            customer = null;
            order = null;
            orderIn = false;

            if(arrivals.Count == 0)
            {
                EndDay();
                return;
            }

            float min = Mathf.Max(0f, Mathf.Min(arrivalDelay.x, arrivalDelay.y));
            float max = Mathf.Max(arrivalDelay.x, arrivalDelay.y);
            nextArrivalAt = Time.time + Random.Range(min, max);
            Say("The counter is empty. Someone's coming.");
            RefreshAll();
        }

        private void NextCustomer()
        {
            overlayOpen = false;
            resultOverlay.SetActive(false);
            dishCost = 0f;
            ClearPlate();

            customer = arrivals[0];
            arrivals.RemoveAt(0);
            order = null;
            orderIn = false;
            orderLine = null;
            orderProfile = FlavourProfile.Zero;
            orderKnown = false;

            Say(customer.name + " is at the counter (" + (servedToday + 1) + " of " + customers.Count + ").");
            BeginVisit();
            RefreshAll();
        }

        /// <summary>The customer has said what they want: the ticket fills in. The recipe book opens on it when you open it.</summary>
        private void SetOrder(RecipeDefinition recipe)
        {
            if(customer == null) return;

            order = recipe;
            orderIn = true;
            orderProfile = order != null ? order.CalculateProfile(rules) : FlavourProfile.Zero;
            orderKnown = IsOnMenu(order);
            OnOrderIn();

            Say(customer.name + " wants " + DishName(order) + ".", UIFactory.Accent);
            RefreshAll();
        }

        /// <summary>Switches the day timer. Mornings only, so a cycle is all one or the other.</summary>
        private void ToggleDayTimer()
        {
            if(phase != Phase.Morning) return;

            dayTimer = !dayTimer;
            Say(dayTimer
                ? "Day timer on: the day ends after " + FormatTime(daySeconds) + ", whoever is still waiting."
                : "Day timer off: take as long as you like. The day ends when everyone has been.");
            RefreshAll();
        }

        /// <summary>Called by the kitchen's Serve button.</summary>
        private void FinishOrder(JudgeResult result)
        {
            money += result.Payment + result.Tip;
            earnedToday += result.Payment + result.Tip;
            servedToday++;
            if(result.IsLiked) likedToday++;

            RelationshipChange change = RecordServe(result);
            ShowResult(result, change);
            ClearPlate();
            RefreshAll();
        }

        /// <summary>Continue on the result screen. An unsaved dish stays Nameless.</summary>
        private void AfterResult()
        {
            resultOverlay.SetActive(false);
            overlayOpen = false;
            namingDish = null;
            // They've left: whatever they were still saying stops with them.
            DialogueSystem.Dialogue.StopAll();
            ClearSpeech();
            ScheduleArrival();
        }

        private void EndDay()
        {
            if(phase != Phase.Service) return;

            phase = Phase.DayOver;
            resultOverlay.SetActive(false);
            if(recipeBookOverlay != null) recipeBookOverlay.SetActive(false);

            // Whoever is still here or still coming leaves unserved.
            List<Customer> missed = new List<Customer>();
            if(customer != null) missed.Add(customer);
            missed.AddRange(arrivals);
            arrivals.Clear();
            customer = null;
            order = null;
            orderIn = false;
            namingDish = null;
            DialogueSystem.Dialogue.StopAll();
            ClearSpeech();
            Say("Cycle " + day + " is over.");

            ReturnBoardToStock();
            ClearKitchen();
            List<Inventory.Entry> spoiled = inventory.SpoilPrepped();

            StringBuilder sb = new StringBuilder();
            if(missed.Count > 0) sb.Append("<color=#e5594d>Time's up.</color> ");
            sb.Append("Served <b>").Append(servedToday).Append("</b> of ").Append(customers.Count)
              .Append(", <b>").Append(likedToday).Append("</b> liked what they got.\n\n");

            // Dishes and who for.
            sb.Append("<b>Dishes</b>\n");
            bool any = false;

            foreach(HistoryEntry entry in history)
            {
                if(entry.Day != day) continue;
                any = true;
                sb.Append("  ").Append(DishName(entry.Dish, entry.Ordered)).Append(" for ").Append(entry.Customer.name)
                  .Append("  <color=#a6a199>").Append(VerdictLabel(entry.Verdict)).Append(" · $").Append(entry.Paid.ToString("0.00")).Append("</color>\n");
            }

            foreach(Customer left in missed) sb.Append("  <color=#e5594d>").Append(left.name).Append(" left without eating.</color>\n");
            if(!any && missed.Count == 0) sb.Append("  <color=#a6a199>None.</color>\n");

            float profit = earnedToday - spentToday - stallRent;
            sb.Append("\nIncome <b>$").Append(earnedToday.ToString("0.00")).Append("</b>\n");
            sb.Append("Expenses (ingredients) <b>$").Append(spentToday.ToString("0.00")).Append("</b>\n");
            sb.Append("Stall rent <b>$").Append(stallRent.ToString("0.00")).Append("</b>\n");
            sb.Append("Profit <b><color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color></b>\n\n");
            sb.Append(DescribeProgressToday());

            if(spoiled.Count > 0)
            {
                float lost = 0f;
                sb.Append("<b>Spoiled overnight</b> <color=#a6a199>(prepped stock doesn't keep)</color>\n");

                foreach(Inventory.Entry entry in spoiled)
                {
                    lost += entry.Ingredient.Price * entry.Quantity;
                    sb.Append("  ").Append(Describe(new IngredientPortion(entry.Ingredient, entry.Quantity, entry.Prep))).Append('\n');
                }

                sb.Append("<color=#e5594d>Worth $").Append(lost.ToString("0.00")).Append("</color>\n\n");
            }

            int rawLeft = 0;
            foreach(Inventory.Entry entry in inventory.Entries()) rawLeft += entry.Quantity;
            sb.Append(rawLeft == 0 ? "The pantry is empty." : rawLeft + " raw unit" + (rawLeft == 1 ? "" : "s") + " carry over.");

            // The rent: pay it or lose the stall.
            gameOver = money + 0.0001f < stallRent;

            if(gameOver)
            {
                sb.Append("\n\n<color=#e5594d><b>You can't pay the rent.</b> You have $").Append(money.ToString("0.00"))
                  .Append(" and the rent is $").Append(stallRent.ToString("0.00")).Append(". The stall is gone.</color>");
                summaryTitle.text = "Game over";
                UIFactory.SetButtonText(summaryButton, "Back to the main menu");
            }
            else
            {
                money -= stallRent;
                sb.Append("\n\nAfter rent: <b>$").Append(money.ToString("0.00")).Append("</b>");
                summaryTitle.text = "Cycle " + day + " is over";
                UIFactory.SetButtonText(summaryButton, "Start cycle " + (day + 1));
            }

            summaryBody.text = sb.ToString();
            summaryOverlay.SetActive(true);
            overlayOpen = true;
            RefreshAll();
        }

        private void NextDay()
        {
            // Game over goes back to the main menu; Start there begins a new game.
            if(gameOver)
            {
                summaryOverlay.SetActive(false);
                ShowTitle();
                return;
            }

            day++;
            StartDay();
        }

        /// <summary>Back to cycle 1 with the starting money, menu and nobody knowing you.</summary>
        private void RestartGame()
        {
            gameOver = false;
            day = 1;
            money = startingMoney;
            menu = new List<RecipeDefinition>(startingMenu);
            deletedDishes.Clear();
            inventory.Clear();
            cart.Clear();
            ReturnBoardToStock();
            inventory.Clear();
            ClearKitchen();
            history.Clear();
            relationships.Clear();
            bookRecipe = null;
            selectedEntry = null;
            InitNpcs();
            StartDay();
        }

        // ------------------------------------------------------------------ Menu

        private bool IsOnMenu(RecipeDefinition recipe)
        {
            return recipe != null && menu.Contains(recipe) && !deletedDishes.Contains(recipe);
        }

        /// <summary>The dishes on the menu (saved and not deleted).</summary>
        private List<RecipeDefinition> VisibleMenu()
        {
            List<RecipeDefinition> visible = new List<RecipeDefinition>();
            foreach(RecipeDefinition recipe in menu) if(IsOnMenu(recipe) && !visible.Contains(recipe)) visible.Add(recipe);
            return visible;
        }

        /// <summary>The name to show for a dish, "Nameless" when the player hasn't named it.</summary>
        private static string DishName(RecipeDefinition recipe, string fallback = "-")
        {
            return recipe != null ? recipe.DisplayName : fallback;
        }

        /// <summary>Soup, fried, or neither, from how its ingredients are cooked.</summary>
        private static string DishType(RecipeDefinition recipe)
        {
            if(recipe == null) return "";

            bool boiled = false;
            bool fried = false;

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;
                boiled |= (portion.cooking & CookMethods.Boiled) != 0;
                fried |= (portion.cooking & CookMethods.Fried) != 0;
            }

            return boiled && fried ? "Soup, with fried bits" : boiled ? "Soup" : fried ? "Fried" : "Neither (raw)";
        }

        // ------------------------------------------------------------------ Areas

        private bool IsAreaOpen(Area target)
        {
            switch(target)
            {
                case Area.Store: return phase == Phase.Morning;
                // Prepping is part of the timed day, while the regulars arrive.
                case Area.Prep: return phase == Phase.Service;
                case Area.Kitchen: return phase == Phase.Service;
                default: return false;
            }
        }

        private void ShowArea(Area target)
        {
            if(!IsAreaOpen(target))
            {
                Say(target == Area.Store ? "The store is closed until tomorrow morning."
                    : target == Area.Prep ? "Prepping starts when the day starts." : "The kitchen opens when the day starts.", UIFactory.Bad);
                return;
            }

            // Leaving the prep board puts what's on it back in stock, prepped as far as it got.
            if(area == Area.Prep && target != Area.Prep && boardUnit != null)
            {
                string done = Describe(boardUnit, true);
                ReturnBoardToStock();
                Say(done + " back in stock.");
            }

            area = target;
            if(recipeBookOverlay != null) recipeBookOverlay.SetActive(false);
            foreach(KeyValuePair<Area, GameObject> pair in areaRoots) pair.Value.SetActive(pair.Key == target);
            RefreshAll();
        }

        private void RefreshAll()
        {
            moneyText.text = "$" + money.ToString("0.00");

            string phaseLabel = phase == Phase.Morning ? "Morning"
                : phase == Phase.Service ? (dayTimer ? FormatTime(dayTimeLeft) + " left" : "Open")
                : "Closed";
            dayText.text = "Cycle " + day + "  ·  " + phaseLabel;

            phaseButton.gameObject.SetActive(phase == Phase.Morning);

            foreach(KeyValuePair<Area, Button> pair in tabs)
            {
                bool open = IsAreaOpen(pair.Key);
                pair.Value.interactable = open;
                pair.Value.GetComponent<Image>().color = pair.Key == area ? new Color(0.75f, 0.42f, 0.15f, 1f) : UIFactory.ButtonColor;
                string label = pair.Key.ToString();
                if(!open) label += pair.Key == Area.Store ? " (closed)" : " (locked)";
                UIFactory.SetButtonText(pair.Value, label);
            }

            RefreshStore();
            RefreshPrep();
            RefreshKitchen();
            RefreshTickets();
            RefreshSidebar();
            RefreshRecipeBook();
        }

        // ------------------------------------------------------------------ Ticket

        private void RefreshTickets()
        {
            foreach(TicketView ticket in tickets)
            {
                bool timer = dayTimer && phase == Phase.Service;
                ticket.TimerBar.SetActive(timer);
                ticket.Timer.gameObject.SetActive(timer);

                if(phase == Phase.Morning)
                {
                    ticket.Customer.text = "No customers yet";
                    ticket.Line.text = "<color=#a6a199>Fill the cart in the Store and prep what you can. Prepped stock spoils overnight; raw keeps. "
                                       + "Each regular comes once today. Rent at the end of the cycle: $" + stallRent.ToString("0.00") + ".</color>";
                    ticket.Order.text = DescribeRegulars();
                }
                else if(phase == Phase.Service && customer != null)
                {
                    ticket.Customer.text = customer.name + "  <size=17><color=#a6a199>" + StageLabel(Relationship(customer).Stage) + "</color></size>";

                    if(orderIn)
                    {
                        ticket.Line.text = string.IsNullOrEmpty(orderLine) ? "" : "\"" + orderLine + "\"";
                        ticket.Order.text = DescribeRecipe(order);
                    }
                    else
                    {
                        ticket.Line.text = "<color=#a6a199>Ordering...</color>";
                        ticket.Order.text = "";
                    }
                }
                else if(phase == Phase.Service)
                {
                    ticket.Customer.text = "Empty counter";
                    ticket.Line.text = "<color=#a6a199>" + (arrivals.Count == 1 ? "1 more regular" : arrivals.Count + " more regulars") + " to come today.</color>";
                    ticket.Order.text = "";
                }
                else
                {
                    ticket.Customer.text = "Closed";
                    ticket.Line.text = "";
                    ticket.Order.text = "";
                }
            }

            UpdateTicketTimers();
        }

        private void UpdateTicketTimers()
        {
            if(phase == Phase.Service && dayTimer && dayText != null)
            {
                dayText.text = "Cycle " + day + "  ·  " + FormatTime(dayTimeLeft) + " left";
            }

            if(!dayTimer) return;

            float share = daySeconds > 0f ? Mathf.Clamp01(dayTimeLeft / daySeconds) : 0f;
            Color color = share < 0.15f ? UIFactory.Bad : share < 0.35f ? UIFactory.Accent : UIFactory.Good;
            string label = FormatTime(dayTimeLeft) + " left today";

            foreach(TicketView ticket in tickets)
            {
                ticket.TimerFill.anchorMax = new Vector2(share, 1f);
                ticket.TimerFillImage.color = color;
                ticket.Timer.text = label;
                ticket.Timer.color = color;
            }
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        /// <summary>Who comes by and what they tend to order, for planning the shop.</summary>
        private string DescribeRegulars()
        {
            StringBuilder sb = new StringBuilder("\n<b>Regulars and what they order</b>\n");

            foreach(Customer regular in customers)
            {
                CustomerRelationship relationship = Relationship(regular);
                sb.Append("<color=#f28c33>").Append(regular.name).Append("</color> <color=#77736d>(")
                  .Append(StageLabel(relationship.Stage)).Append(")</color>: ");

                List<string> dishes = new List<string>();
                if(relationship.DreamMeal != null) dishes.Add(DishName(relationship.DreamMeal) + " <color=#f28c33>(dream)</color>");
                foreach(RecipeDefinition liked in relationship.LikedMeals) dishes.Add(DishName(liked));

                sb.Append(dishes.Count == 0 ? "anything on the menu" : string.Join(", ", dishes)).Append('\n');
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------ Text helpers

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
                if(markRaw && portion.prep == PrepSteps.None) parts.Add("raw");
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

            // Not in the book: just the name. A dish you made them before is in the cooking history;
            // one only they know, you have to work out.
            if(!IsOnMenu(recipe))
            {
                string hint = IsPlayerDish(recipe)
                    ? "Not in your recipe book: it's something you made for them before. Find it in the cooking history."
                    : "Not in your recipe book. You'll have to work out what goes in it.";
                return "<size=24><b>" + recipe.DisplayName + "</b></size>\n<color=#a6a199>" + hint + "</color>";
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=24><b>").Append(recipe.DisplayName).Append("</b></size>\n");
            sb.Append("<color=#a6a199>").Append(DishType(recipe));
            if(!string.IsNullOrEmpty(recipe.Origin)) sb.Append(" · Origin: ").Append(recipe.Origin);
            if(!IsOnMenu(recipe)) sb.Append(" · not on the menu");
            sb.Append("</color>\n\n");

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;

                // Whether stock (plus whatever is on the prep board) is ready exactly as the recipe wants it prepped.
                bool onBoard = boardUnit != null && boardUnit.ingredient == portion.ingredient;
                int ready = inventory.Count(portion.ingredient, portion.prep) + (onBoard && boardUnit.prep == portion.prep ? 1 : 0);
                int total = inventory.Total(portion.ingredient) + (onBoard ? 1 : 0);
                string readiness = ready >= portion.quantity ? "<color=#73cc73>ready</color>"
                    : total >= portion.quantity ? "<color=#f28c33>needs prep</color>"
                    : "<color=#e5594d>short</color>";
                sb.Append("- ").Append(Describe(portion, true)).Append("  ").Append(readiness).Append('\n');
            }

            sb.Append("<color=#a6a199>Steps are in the Recipe book.</color>\n");
            sb.Append("\nMenu price <b>$").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00")).Append("</b>");

            return sb.ToString();
        }

        private void Say(string text, Color? color = null)
        {
            if(messageText == null) return;
            messageText.text = text;
            messageText.color = color ?? UIFactory.TextColor;
        }

        /// <summary>"Similar to the order: 76% (flavour 82%, ingredients 70%) → Close enough".</summary>
        private string DescribeSimilarity(JudgeResult result)
        {
            if(result.ExactMatch) return "Similar to the order: <color=#73cc73>exact</color>";

            Similarity s = result.Similarity;
            string tier = s.Combined >= veryCloseAt ? "<color=#73cc73>very close</color>"
                : s.Combined >= closeEnoughAt ? "<color=#f28c33>close enough</color>"
                : "<color=#e5594d>too different</color>";

            return "Similar to the order: <b>" + Percent(s.Combined) + "</b> " + tier
                   + "\n<color=#a6a199>  flavour " + Percent(s.Flavour) + ", ingredients " + Percent(s.Ingredients) + "</color>";
        }

        private static string Percent(float value)
        {
            return (value * 100f).ToString("0") + "%";
        }

        // ------------------------------------------------------------------ Result

        private void ShowResult(JudgeResult result, RelationshipChange change)
        {
            overlayOpen = true;
            if(recipeBookOverlay != null) recipeBookOverlay.SetActive(false);
            resultOverlay.SetActive(true);
            resultScroll.anchoredPosition = Vector2.zero;

            string title;
            string line;
            Color color;

            switch(result.Verdict)
            {
                case Verdict.DreamMeal:
                    title = change.FoundDream ? "Their dream meal!" : "Their dream meal, again"; line = customer.dreamLine; color = UIFactory.Accent; break;
                case Verdict.Ordered:
                    title = "Just what they ordered. They liked it"; line = customer.likedLine; color = UIFactory.Good; break;
                case Verdict.VeryClose:
                    title = "Wrong order, good food. They liked it"; line = customer.likedLine; color = UIFactory.Good; break;
                case Verdict.CloseEnough:
                    title = "Wrong order, good food. They pay, but not all of it"; line = "It's... fine."; color = UIFactory.Accent; break;
                case Verdict.Refused:
                    title = "Wrong order, bad food. They won't eat it"; line = customer.refusedLine; color = UIFactory.Bad; break;
                default:
                    title = "Wrong order, bad food. They won't pay"; line = customer.dislikedLine; color = UIFactory.Bad; break;
            }

            resultTitle.text = title;
            resultTitle.color = color;

            // What they say comes from the dialogue catalog (it plays right after anything they were
            // saying), or the customer's fallback line when the catalog has nothing for this moment.
            Speak(CustomerDialogue.EventFor(result, change.FoundDream || result.Verdict != Verdict.DreamMeal), line, priority: true);

            StringBuilder sb = new StringBuilder();
            sb.Append(DescribeChange(change));
            sb.Append("<b>Ordered</b>  ").Append(DishName(order)).Append('\n');
            sb.Append("<b>Served</b>\n");
            foreach(IngredientPortion portion in plate) sb.Append("  ").Append(Describe(portion, true)).Append('\n');

            sb.Append("\n<b>Flavour profile</b>\n<size=17>").Append(result.Dish.ToString()).Append("</size>\n\n");
            sb.Append(DescribeSimilarity(result)).Append('\n');
            if(result.RefusedIngredient != null) sb.Append("Won't eat: <color=#e5594d>").Append(result.RefusedIngredient.DisplayName).Append("</color>\n");
            if(showDesireDebug && result.DesireScore >= 0f) sb.Append("<color=#a6a199>Secret desire (debug): ").Append((result.DesireScore * 100f).ToString("0")).Append("%</color>\n");

            float profit = result.Payment + result.Tip - dishCost;
            sb.Append("\nPaid <b>$").Append(result.Payment.ToString("0.00")).Append("</b>");
            if(result.IsPaid && result.PayShare < 1f) sb.Append("  <color=#a6a199>(").Append((result.PayShare * 100f).ToString("0")).Append("% of $").Append(result.Price.ToString("0.00")).Append(")</color>");
            if(result.Tip > 0f) sb.Append("  + tip <b>$").Append(result.Tip.ToString("0.00")).Append("</b>");
            sb.Append("\nIngredients used $").Append(dishCost.ToString("0.00"));
            sb.Append("\nProfit <b><color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color></b>");
            resultBody.text = sb.ToString();

            // Only your dish, and the order when you knew what it was.
            resultRadar.ClearProfiles();
            if(orderKnown) resultRadar.AddProfile(result.Ordered, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            resultRadar.AddProfile(result.Dish, UIFactory.Accent, true);

            ShowNaming(currentEntry != null ? currentEntry.Dish : null);
            UIFactory.SetButtonText(resultButton, arrivals.Count == 0 ? "Continue (close for the day)" : "Continue");
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
            BuildTopBar(root);

            RectTransform bottom = UIFactory.Panel(root, "Message Bar", UIFactory.PanelDark).rectTransform;
            bottom.anchorMin = Vector2.zero;
            bottom.anchorMax = new Vector2(1f, 0f);
            bottom.offsetMin = Vector2.zero;
            bottom.offsetMax = new Vector2(0f, 44f);
            messageText = UIFactory.Label(bottom, "", 20, TextAnchor.MiddleLeft);
            UIFactory.Stretch(messageText.rectTransform).offsetMin = new Vector2(20f, 0f);

            // The areas take the left of the screen; what customers say runs down the right.
            RectTransform content = UIFactory.Rect(root, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = new Vector2(1f - SidebarWidth, 1f);
            content.offsetMin = new Vector2(0f, 44f);
            content.offsetMax = new Vector2(0f, -60f);

            areaRoots[Area.Store] = BuildStoreArea(AreaRoot(content, "Store Area"));
            areaRoots[Area.Prep] = BuildPrepArea(AreaRoot(content, "Prep Area"));
            areaRoots[Area.Kitchen] = BuildKitchenArea(AreaRoot(content, "Kitchen Area"));

            // The book sits over the areas (not the sidebar); result and summary screens go on top.
            BuildSpeechSidebar(root);
            BuildRecipeBook(root);
            BuildResultOverlay(root);
            BuildSummaryOverlay(root);
            BuildTitle(root);
        }

        private static RectTransform AreaRoot(RectTransform parent, string name)
        {
            RectTransform areaRoot = UIFactory.Rect(parent, name);
            UIFactory.Stretch(areaRoot);
            return areaRoot;
        }

        private void BuildTopBar(Transform root)
        {
            RectTransform top = UIFactory.Panel(root, "Top Bar", UIFactory.PanelDark).rectTransform;
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = Vector2.one;
            top.offsetMin = new Vector2(0f, -60f);
            top.offsetMax = Vector2.zero;

            Text title = UIFactory.Label(top, "Emphis's Stall", 28, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Place(title.rectTransform, 0f, 0f, 0.15f, 1f).offsetMin = new Vector2(20f, 0f);

            RectTransform tabRow = UIFactory.Rect(top, "Tabs");
            UIFactory.Place(tabRow, 0.15f, 0f, 0.43f, 1f, 8f);
            UIFactory.Horizontal(tabRow.gameObject, 8f);
            tabs[Area.Store] = UIFactory.Button(tabRow, "Store", () => ShowArea(Area.Store), null, 20);
            tabs[Area.Prep] = UIFactory.Button(tabRow, "Prep", () => ShowArea(Area.Prep), null, 20);
            tabs[Area.Kitchen] = UIFactory.Button(tabRow, "Kitchen", () => ShowArea(Area.Kitchen), null, 20);

            // Always available, whichever area you're in.
            recipeBookButton = UIFactory.Button(top, "Recipe book", ToggleRecipeBook, new Color(0.3f, 0.36f, 0.5f, 1f), 20);
            UIFactory.Place((RectTransform)recipeBookButton.transform, 0.43f, 0f, 0.55f, 1f, 8f);

            dayText = UIFactory.Label(top, "", 22, TextAnchor.MiddleCenter);
            UIFactory.Place(dayText.rectTransform, 0.55f, 0f, 0.68f, 1f);

            phaseButton = UIFactory.Button(top, "Buy ingredients and start the day", OpenStall, new Color(0.75f, 0.42f, 0.15f, 1f), 18);
            UIFactory.Place((RectTransform)phaseButton.transform, 0.68f, 0f, 0.88f, 1f, 8f);

            moneyText = UIFactory.Label(top, "", 30, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);
            UIFactory.Place(moneyText.rectTransform, 0.88f, 0f, 1f, 1f).offsetMax = new Vector2(-20f, 0f);
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
            UIFactory.Size(header, 22f, 0f);
            return header;
        }

        /// <summary>The customer / order ticket with the day timer, filling <paramref name="column"/>.</summary>
        private void BuildTicket(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;

            TicketView ticket = new TicketView();
            Header(column, "CUSTOMER");
            ticket.Customer = UIFactory.Label(column, "", 28, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            ticket.Line = UIFactory.Label(column, "", 18, TextAnchor.UpperLeft, FontStyle.Italic);

            Image timerBack = UIFactory.Panel(column, "Timer", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(timerBack, 16f, 0f);
            ticket.TimerBar = timerBack.gameObject;
            ticket.TimerFillImage = UIFactory.Panel(timerBack.transform, "Fill", UIFactory.Good);
            ticket.TimerFill = ticket.TimerFillImage.rectTransform;
            UIFactory.Place(ticket.TimerFill, 0f, 0f, 1f, 1f);
            ticket.Timer = UIFactory.Label(column, "", 20, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(ticket.Timer, 28f, 0f);

            ticket.Order = UIFactory.Label(column, "", 17);
            tickets.Add(ticket);
        }

        /// <summary>A dark pad that feeds pointer gestures to the given callbacks and draws guides and a trail.</summary>
        private static GesturePadGraphic BuildPad(Transform parent, string name, float minHeight, float flexibleHeight,
            System.Action<Vector2> pressed, System.Action<Vector2> moved, System.Action released)
        {
            Image background = UIFactory.Panel(parent, name, new Color(0.09f, 0.085f, 0.1f, 1f));
            UIFactory.Size(background, minHeight, flexibleHeight);
            background.raycastTarget = false;

            RectTransform padRect = UIFactory.Rect(background.transform, "Pad");
            UIFactory.Stretch(padRect);
            GesturePadGraphic graphic = padRect.gameObject.AddComponent<GesturePadGraphic>();
            graphic.raycastTarget = true;
            GesturePad pad = padRect.gameObject.AddComponent<GesturePad>();
            pad.Pressed += pressed;
            pad.Moved += moved;
            pad.Released += released;
            return graphic;
        }

        /// <summary>Rebuilds a stock list: one button per inventory entry.</summary>
        private void RebuildStockList(Transform content, System.Action<Inventory.Entry> onClick, bool interactable)
        {
            UIFactory.ClearChildren(content);
            List<Inventory.Entry> entries = inventory.Entries();

            if(entries.Count == 0)
            {
                Text empty = UIFactory.Label(content, "<color=#a6a199>No stock. Everything comes from the store in the morning.</color>", 17);
                UIFactory.Size(empty, 50f, 0f);
                return;
            }

            foreach(Inventory.Entry entry in entries)
            {
                Inventory.Entry captured = entry;
                string label = Describe(new IngredientPortion(entry.Ingredient, entry.Quantity, entry.Prep), true);
                RectTransform row = UIFactory.Row(content, 38f, 4f);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                UIFactory.Icon(row, entry.Ingredient.Icon, 38f);

                // Prepped and raw stock look the same: colour would suggest only prepped stock goes in a dish.
                Button button = UIFactory.Button(row, label, () => onClick(captured), UIFactory.ButtonColor, 17);
                Text text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                button.interactable = interactable;
                UIFactory.Size(button, 38f, 0f, -1f, 1f);
            }
        }

        private void BuildResultOverlay(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Result Overlay", new Color(0f, 0f, 0f, 0.78f));
            UIFactory.Stretch(overlay.rectTransform);
            resultOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Result", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1500f, 920f);

            RectTransform radarArea = UIFactory.Rect(panel, "Radar");
            UIFactory.Place(radarArea, 0f, 0f, 0.45f, 1f, 30f);
            resultRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            resultRadar.raycastTarget = false;
            resultRadar.CreateLabels(UIFactory.Font, 18, UIFactory.MutedText);

            RectTransform right = UIFactory.Rect(panel, "Details");
            UIFactory.Place(right, 0.45f, 0f, 1f, 1f, 30f);
            VerticalLayoutGroup layout = UIFactory.Vertical(right.gameObject, 12f, 0);
            layout.childForceExpandHeight = false;

            resultTitle = UIFactory.Label(right, "", 30, TextAnchor.UpperLeft, FontStyle.Bold);

            // What they say and the report scroll; the title, name field and Continue stay put.
            resultScroll = UIFactory.ScrollList(right, 14f);
            resultSpeech = UIFactory.Label(resultScroll, "", 20, TextAnchor.UpperLeft, FontStyle.Italic);
            resultBody = UIFactory.Label(resultScroll, "", 18);

            BuildNamingRow(right);

            RectTransform row = UIFactory.Row(right, 56f);
            resultButton = UIFactory.Button(row, "Continue", AfterResult, new Color(0.75f, 0.42f, 0.15f, 1f), 24);

            resultOverlay.SetActive(false);
        }

        private void BuildSummaryOverlay(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Summary Overlay", new Color(0f, 0f, 0f, 0.82f));
            UIFactory.Stretch(overlay.rectTransform);
            summaryOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Summary", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(820f, 940f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 14f, 32);
            layout.childForceExpandHeight = false;

            summaryTitle = UIFactory.Label(panel, "", 36, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            RectTransform scroll = UIFactory.ScrollList(panel, 0f);
            summaryBody = UIFactory.Label(scroll, "", 19);
            RectTransform row = UIFactory.Row(panel, 56f);
            summaryButton = UIFactory.Button(row, "Start next cycle", NextDay, new Color(0.75f, 0.42f, 0.15f, 1f), 24);

            summaryOverlay.SetActive(false);
        }
    }
}
