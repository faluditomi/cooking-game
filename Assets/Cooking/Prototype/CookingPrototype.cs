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
        [Tooltip("Dishes only NPCs know (the Sleeper's potato): they can order them by name, but they're not on the menu or in the book.")]
        [SerializeField] private List<RecipeDefinition> offMenu = new List<RecipeDefinition>();
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
        [Tooltip("Chance a regular stays away for a cycle or two after food they didn't pay for (they always come back).")]
        [SerializeField, Range(0f, 1f)] private float stayAwayChance = 0.5f;

        [Header("Orders")]
        [Tooltip("Menu price = ingredient cost × margin.")]
        [SerializeField, Min(0f)] private float margin = 2.4f;
        [Header("Good enough (how close a dish must be to the order)")]
        [Tooltip("Share of similarity that comes from flavour (radar distance); the rest comes from ingredients, prep and cooking.")]
        [SerializeField, Range(0f, 1f)] private float flavourWeight = 0.5f;
        [Tooltip("Radar distance at which flavour similarity drops to 0.")]
        [SerializeField, Min(0.5f)] private float flavourRange = 6f;
        [SerializeField, Range(0f, 1f)] private float veryCloseAt = 0.7f;
        [Tooltip("Share of the price paid for a very close dish.")]
        [SerializeField, Range(0f, 1f)] private float veryClosePay = 0.8f;
        [Tooltip("Below this similarity they refuse to pay.")]
        [SerializeField, Range(0f, 1f)] private float closeEnoughAt = 0.45f;
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
        [Tooltip("Show how close a dish came to the customer's secret desire on the result screen. Tuning only; it gives the secret away.")]
        [SerializeField] private bool showDesireDebug;
        [Tooltip("0 picks a random seed.")]
        [SerializeField] private int randomSeed;

        private enum Phase
        {
            Morning,
            Service,
            DayOver,
        }

        /// <summary>The order ticket, on the tablet's Order page.</summary>
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
        /// <summary>How many regulars come today (some may be staying away).</summary>
        private int todaysRegulars;
        /// <summary>Time's up, but the customer at the counter still gets their order; nobody else comes.</summary>
        private bool closing;
        /// <summary>Regulars staying away after bad food, and for how many more cycles.</summary>
        private readonly Dictionary<Customer, int> awayFor = new Dictionary<Customer, int>();
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
        /// <summary>Cycles the current customer will now stay away (0 if they're coming back as usual), for the result page.</summary>
        private int stayingAway;

        // Shared UI
        private Text dayText;
        private Text moneyText;
        private Text messageText;
        private Button phaseButton;
        private readonly List<TicketView> tickets = new List<TicketView>();
        private GameObject resultOverlay;
        private Text resultTitle;
        private Text resultBody;
        private RectTransform resultScroll;
        private RectTransform resultPicture;
        private FlavourRadarGraphic resultRadar;
        private Button resultButton;
        private GameObject summaryOverlay;
        private Text summaryTitle;
        private Text summaryBody;
        private Button summaryButton;

        private float Falloff => rules != null ? rules.zoneFalloff : FlavourRules.DefaultZoneFalloff;

        /// <summary>Assigns the data. Used by the editor setup menu.</summary>
        public void Setup(FlavourRules newRules, IEnumerable<FlavourZone> newZones, IEnumerable<IngredientDefinition> newPantry,
            IEnumerable<RecipeDefinition> newMenu, IEnumerable<Customer> newCustomers, IEnumerable<RecipeDefinition> newOffMenu = null)
        {
            offMenu = newOffMenu != null ? new List<RecipeDefinition>(newOffMenu) : new List<RecipeDefinition>();
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

            // Secret desires are undiscovered recipes: never drawn, listed or named as a zone (until found).
            zones.RemoveAll(zone => zone == null || customers.Exists(c => c != null && c.secretDesire == zone));
            ResetTasteProfiles();

            CreateKitchenState();
            EnsureEventSystem();
            ApplySkin();
            InitAudio();
            BuildUI();
            InitNpcs();
            StartDay();
            ShowTitle();
        }

        private void Update()
        {
            UpdatePauseKey();
            if(paused) return;

            // Lines keep typing out on the result screen too.
            TickSpeech(Time.deltaTime);
            // Whatever is held follows the pointer, and drops when the button comes up.
            UpdateHeld();
            UpdateShopDrag();
            UpdateCookingSounds();
            UpdateTutorial();

            if(overlayOpen || phase != Phase.Service) return;

            // The kitchen keeps cooking and the clock keeps running whichever area you're in.
            TickKitchen(Time.deltaTime);

            if(dayTimer && !closing && !TutorialActive)
            {
                dayTimeLeft -= Time.deltaTime;

                if(dayTimeLeft <= 0f && !closing)
                {
                    dayTimeLeft = 0f;

                    if(customer == null)
                    {
                        EndDay();
                        return;
                    }

                    // The one at the counter still gets their order; nobody else comes today.
                    closing = true;
                    Say("Time's up. Finish " + customer.name + "'s order; nobody else is coming today.", UIFactory.Accent);
                    RefreshAll();
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

            closing = false;

            // Everyone comes once a cycle, in a new order each time, except anyone staying away
            // after bad food (design doc: they miss a day or two, but always come back).
            arrivals.Clear();
            List<Customer> away = new List<Customer>();

            foreach(Customer regular in customers)
            {
                if(awayFor.TryGetValue(regular, out int cycles) && cycles > 0)
                {
                    awayFor[regular] = cycles - 1;
                    away.Add(regular);
                }
                else
                {
                    arrivals.Add(regular);
                }
            }

            // Never an empty day: someone always turns up.
            if(arrivals.Count == 0 && away.Count > 0)
            {
                Customer back = away[random.Next(away.Count)];
                awayFor[back] = 0;
                away.Remove(back);
                arrivals.Add(back);
            }

            todaysRegulars = arrivals.Count;
            for(int i = arrivals.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (arrivals[i], arrivals[j]) = (arrivals[j], arrivals[i]);
            }

            tabletPage = TabletPage.Order;
            RefreshAll();
            Say("Cycle " + day + ". Fill the cart for " + todaysRegulars + (todaysRegulars == 1 ? " regular" : " regulars")
                + (away.Count > 0 ? " (" + string.Join(", ", away.ConvertAll(c => c.name)) + " won't come today)" : "")
                + ", then buy and start the day. Prepping starts with the day.");
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
                    Say("You can't afford the basket: $" + total.ToString("0.00") + ", and you have $" + money.ToString("0.00") + ". Take something out first.", UIFactory.Bad);
                    return;
                }

                spentToday += total;
                Play(sounds.register, 0.8f, 0f);
                Say("Bought " + units + " units for $" + total.ToString("0.00") + ".", UIFactory.Good);
            }

            if(inventory.IsEmpty && !DeskHasIngredients())
            {
                Say("You have nothing to cook with. Put something in the basket first.", UIFactory.Bad);
                return;
            }

            phase = Phase.Service;
            // Back from the market to the stall; the tablet shows the order.
            tabletPage = TabletPage.Order;
            ScheduleArrival();
        }

        /// <summary>Empties the counter; the next customer comes a few seconds later, or the day ends.</summary>
        private void ScheduleArrival()
        {
            customer = null;
            order = null;
            orderIn = false;

            if(arrivals.Count == 0 || closing)
            {
                EndDay();
                return;
            }

            float min = Mathf.Max(0f, Mathf.Min(arrivalDelay.x, arrivalDelay.y));
            float max = Mathf.Max(arrivalDelay.x, arrivalDelay.y);
            nextArrivalAt = Time.time + Random.Range(min, max);
            RefreshAll();
        }

        private void NextCustomer()
        {
            overlayOpen = false;
            resultOverlay.SetActive(false);
            dishCost = 0f;
            stayingAway = 0;
            ClearPlate();

            customer = arrivals[0];
            arrivals.RemoveAt(0);
            order = null;
            orderIn = false;
            orderLine = null;
            orderProfile = FlavourProfile.Zero;
            orderKnown = false;

            Say(customer.name + " is at the counter (" + (servedToday + 1) + " of " + todaysRegulars + ").");
            Play(sounds.arrive, 0.6f, 0f);
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
            MaybeStayAway(result);
            ShowResult(result, change);
            ClearPlate();
            RefreshAll();
        }

        /// <summary>
        /// After bad food (not paid for, or refused) a regular may stay away for a cycle or two
        /// (design doc). They always come back. Someone who doesn't know food yet doesn't mind.
        /// </summary>
        private void MaybeStayAway(JudgeResult result)
        {
            bool bad = !result.IsPaid || result.Verdict == Verdict.Refused;
            if(!bad || result.PaidAnyway || customer == null || random.NextDouble() >= stayAwayChance) return;

            int cycles = random.Next(1, 3);
            awayFor[customer] = cycles;
            stayingAway = cycles;
            progressToday.Add(customer.name + " didn't like it and will stay away for " + (cycles == 1 ? "a cycle." : "a couple of cycles."));
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

            if(practiceDay)
            {
                FinishPracticeDay();
                return;
            }

            phase = Phase.DayOver;
            resultOverlay.SetActive(false);
            tabletPage = TabletPage.Order;

            // Whoever is still here or still coming leaves unserved.
            List<Customer> missed = new List<Customer>();
            if(customer != null) missed.Add(customer);
            missed.AddRange(arrivals);
            // Who never got to the counter (rather than leaving it hungry).
            List<Customer> neverCame = new List<Customer>(arrivals);
            arrivals.Clear();
            customer = null;
            order = null;
            orderIn = false;
            namingDish = null;
            DialogueSystem.Dialogue.StopAll();
            ClearSpeech();
            Say("Cycle " + day + " is over.");

            ReturnDeskToStock();
            ClearKitchen();
            List<Inventory.Entry> spoiled = inventory.SpoilPrepped();

            StringBuilder sb = new StringBuilder();
            if(missed.Count > 0) sb.Append("<color=#e5594d>Time's up.</color> ");
            sb.Append("Served <b>").Append(servedToday).Append("</b> of ").Append(todaysRegulars)
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

            foreach(Customer left in missed)
            {
                sb.Append("  <color=#e5594d>").Append(left.name).Append(neverCame.Contains(left) ? " didn't make it before closing." : " left without eating.").Append("</color>\n");
            }
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
            Play(sounds.dayEnd, 0.8f, 0f);
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
            ReturnDeskToStock();
            inventory.Clear();
            ClearKitchen();
            EndTutorial();
            history.Clear();
            relationships.Clear();
            awayFor.Clear();
            bookRecipe = null;
            selectedEntry = null;
            ResetTasteProfiles();
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

        /// <summary>Soup, fried, or raw, from how its ingredients are cooked.</summary>
        private static string DishType(RecipeDefinition recipe)
        {
            return recipe != null ? DishTypeOf(recipe.Portions) : "";
        }

        /// <summary>Soup, fried, or raw, for what's on a plate.</summary>
        private static string DishTypeOf(IReadOnlyList<IngredientPortion> portions)
        {
            bool boiled = false;
            bool fried = false;

            foreach(IngredientPortion portion in portions)
            {
                if(portion == null || !portion.IsValid) continue;
                // Boiled and then fried (like the rice in fried rice) ends up fried.
                bool friedToo = (portion.cooking & CookMethods.Fried) != 0;
                boiled |= (portion.cooking & CookMethods.Boiled) != 0 && !friedToo;
                fried |= friedToo;
            }

            return boiled && fried ? "Soup, with fried bits" : boiled ? "Soup" : fried ? "Fried" : "Raw";
        }

        private void RefreshAll()
        {
            moneyText.text = "$" + money.ToString("0.00");

            string phaseLabel = phase == Phase.Morning ? "Morning"
                : phase == Phase.Service ? (closing ? "Last customer" : dayTimer ? FormatTime(dayTimeLeft) + " left" : "Open")
                : "Closed";
            dayText.text = practiceDay ? "Practice day" : "Cycle " + day;

            phaseButton.interactable = phase == Phase.Morning;

            RefreshTablet();
            RefreshStore();
            RefreshKitchen();
            RefreshTickets();
            RefreshSidebar();
            RefreshMarket();
            RefreshRecipeBook();
            RefreshFlavours();
            ApplyOrderZones();
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
                    ticket.Line.text = "<color=#a6a199>Fill the cart in the Shop. Prepped stock spoils overnight; raw keeps. "
                                       + "Each regular comes once today. Rent at the end of the cycle: $" + stallRent.ToString("0.00") + ".</color>";
                    ticket.Order.text = DescribeRegulars();
                }
                else if(phase == Phase.Service && customer != null)
                {
                    ticket.Customer.text = customer.name;

                    if(orderIn)
                    {
                        // What they said is in the conversation.
                        ticket.Line.text = "";
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
            if(phase == Phase.Service && dayTimer && dayText != null && !practiceDay)
            {
                dayText.text = "Cycle " + day;
            }

            if(!dayTimer) return;

            float share = daySeconds > 0f ? Mathf.Clamp01(dayTimeLeft / daySeconds) : 0f;
            Color color = share < 0.15f ? UIFactory.Bad : share < 0.35f ? UIFactory.Accent : UIFactory.Good;
            string label = closing ? "Last customer" : FormatTime(dayTimeLeft) + " left today";

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
                sb.Append("<color=#f28c33>").Append(regular.name).Append("</color>: ");

                if(phase == Phase.Morning && !arrivals.Contains(regular))
                {
                    sb.Append("<color=#e5594d>staying away today</color>\n");
                    continue;
                }

                List<string> dishes = new List<string>();
                if(relationship.DreamMeal != null) dishes.Add(DishName(relationship.DreamMeal) + " <color=#f28c33>(dream)</color>");
                foreach(RecipeDefinition liked in relationship.LikedMeals) dishes.Add(DishName(liked));

                string none = regular.firstTasteReplacesLikes && !relationship.HasTasted ? "whatever comes to mind" : "anything on the menu";
                sb.Append(dishes.Count == 0 ? none : string.Join(", ", dishes)).Append('\n');
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

            return "<size=24><b>" + recipe.DisplayName + "</b></size>\n<color=#a6a199>" + DishType(recipe) + "</color>\n\n" + RecipeLines(recipe);
        }

        /// <summary>
        /// A recipe's lines as the order card writes them: each ingredient with its prep and whether
        /// it's ready (in stock as it should be, or already cooking), then the menu price.
        /// </summary>
        private string RecipeLines(RecipeDefinition recipe)
        {
            StringBuilder sb = new StringBuilder();

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;

                // Whether stock (plus whatever is on the prep board) is ready exactly as the recipe wants it prepped.
                int ready = inventory.Count(portion.ingredient, portion.prep) + OnDesk(portion.ingredient, portion.prep);
                int total = inventory.Total(portion.ingredient) + OnDesk(portion.ingredient);
                string cooking = InStation(portion.ingredient, portion.prep, portion.quantity);
                string readiness = cooking != null ? "<color=#73cc73>" + cooking + "</color>"
                    : ready >= portion.quantity ? "<color=#73cc73>ready</color>"
                    : total >= portion.quantity ? "<color=#f28c33>needs prep</color>"
                    : "";
                // Prep only: how it's cooked is the dish type above, and repeating it wraps the narrow ticket.
                sb.Append("- ").Append(Describe(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep))).Append("  ").Append(readiness).Append('\n');
            }

            sb.Append("\nMenu price <b>$").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00")).Append("</b>");

            return sb.ToString();
        }

        /// <summary>Feedback for the player. Only warnings show (a short toast over the desk); the rest of the old hint text is gone.</summary>
        private void Say(string text, Color? color = null)
        {
            if(color.HasValue && color.Value == UIFactory.Bad) ShowToast(text, UIFactory.TextColor);
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
            tabletPage = TabletPage.Order;
            resultOverlay.SetActive(true);
            resultScroll.anchoredPosition = Vector2.zero;

            // The headline is one of three: the order, a different dish they liked, or one they didn't.
            string line;
            string title;
            Color color;
            bool liked = change.FirstTaste || (result.IsPaid && !result.PaidAnyway && result.Verdict != Verdict.CloseEnough) || result.Verdict == Verdict.DreamMeal;

            if(result.ExactMatch && result.Verdict != Verdict.Refused)
            {
                title = "Correct order";
                color = UIFactory.Good;
                line = result.Verdict == Verdict.DreamMeal ? customer.dreamLine : customer.likedLine;
            }
            else if(liked)
            {
                title = "Wrong order, but they liked it";
                color = UIFactory.Accent;
                line = result.Verdict == Verdict.DreamMeal ? customer.dreamLine : customer.likedLine;
            }
            else
            {
                title = "Wrong order and they didn't like it";
                color = UIFactory.Bad;
                line = result.Verdict == Verdict.Refused ? customer.refusedLine : customer.dislikedLine;
            }

            resultTitle.text = title;
            resultTitle.color = color;
            PlayVerdict(result);
            BuildDishPicture(resultPicture, plate);

            // What they say comes from the dialogue catalog (it plays right after anything they were
            // saying), or the customer's fallback line when the catalog has nothing for this moment.
            DialogueSystem.DialogueEventType reaction = change.FirstTaste ? DialogueSystem.DialogueEventType.CorrectOrder
                : CustomerDialogue.EventFor(result, change.FoundDream || result.Verdict != Verdict.DreamMeal);
            Speak(reaction, line, priority: true);

            StringBuilder sb = new StringBuilder();
            sb.Append(DescribeChange(change));
            if(sb.Length > 0) sb.Append('\n');
            AppendOrderAndReceipt(sb, DishName(order), plate, result.Price, result.Payment + result.Tip, dishCost);
            if(result.Tip > 0f) sb.Append("  <color=#a6a199>(with a $").Append(result.Tip.ToString("0.00")).Append(" tip)</color>\n");
            if(result.RefusedIngredient != null) sb.Append("\nWon't eat: <color=#e5594d>").Append(result.RefusedIngredient.DisplayName).Append("</color>\n");
            if(showDesireDebug && result.DesireScore >= 0f) sb.Append("<color=#a6a199>Favourite flavour (debug): ").Append((result.DesireScore * 100f).ToString("0")).Append("%</color>\n");
            resultBody.text = sb.ToString();

            // The dish on the radar; no zone (the nearest one was misleading when the dish wasn't in it).
            resultRadar.ClearProfiles();
            resultRadar.SetZones(null);
            // The order's outline, when it's a dish you know, to compare the taste with.
            if(orderKnown) resultRadar.AddProfile(result.Ordered, new Color(0.85f, 0.85f, 0.85f, 0.8f), false);
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

        /// <summary>
        /// The Dish Served page, on the tablet (the tabs hide while it's up): the dish's picture, its
        /// flavour profile with the tasty zone it fell in, what was ordered and received, the receipt,
        /// and a chance to save the dish before Continue.
        /// </summary>
        private void BuildResultOverlay(Transform pages)
        {
            Image overlay = UIFactory.Panel(pages, "Dish Served", UIFactory.PanelColor);
            UIFactory.Stretch(overlay.rectTransform);
            resultOverlay = overlay.gameObject;
            RectTransform panel = overlay.rectTransform;

            RectTransform left = UIFactory.Rect(panel, "Picture and Flavour");
            UIFactory.Place(left, 0f, 0f, 0.4f, 1f, 6f);
            VerticalLayoutGroup leftLayout = UIFactory.Vertical(left.gameObject, 2f, 0);
            leftLayout.childForceExpandHeight = false;
            leftLayout.childAlignment = TextAnchor.UpperCenter;
            leftLayout.childForceExpandWidth = false;
            resultPicture = UIFactory.Rect(left, "Dish Picture");
            UIFactory.Size(resultPicture, 84f, 0f, 84f, 0f);
            RectTransform radarArea = UIFactory.Rect(left, "Radar");
            UIFactory.Size(radarArea, 120f, 1f, 200f, 1f);
            resultRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            resultRadar.raycastTarget = false;
            resultRadar.SetLabelMargin(26f);
            resultRadar.CreateLabels(UIFactory.Font, 10, UIFactory.MutedText);

            RectTransform right = UIFactory.Rect(panel, "Details");
            UIFactory.Place(right, 0.4f, 0f, 1f, 1f, 6f);
            VerticalLayoutGroup layout = UIFactory.Vertical(right.gameObject, 4f, 0);
            layout.childForceExpandHeight = false;

            resultTitle = UIFactory.Label(right, "", 15, TextAnchor.UpperLeft, FontStyle.Bold);
            UIFactory.Size(resultTitle, 40f, 0f);
            UIFactory.FitText(resultTitle);

            resultScroll = UIFactory.ScrollList(right, 4f);
            resultBody = UIFactory.Label(resultScroll, "", 13);

            BuildNamingRow(right);

            RectTransform row = UIFactory.Row(right, 34f);
            resultButton = UIFactory.Button(row, "Continue", AfterResult, Orange, 16);

            resultOverlay.SetActive(false);
        }

        /// <summary>The End of Cycle page, on the tablet.</summary>
        private void BuildSummaryOverlay(Transform pages)
        {
            Image overlay = UIFactory.Panel(pages, "End of Cycle", UIFactory.PanelColor);
            UIFactory.Stretch(overlay.rectTransform);
            summaryOverlay = overlay.gameObject;
            VerticalLayoutGroup layout = UIFactory.Vertical(overlay.gameObject, 6f, 10);
            layout.childForceExpandHeight = false;

            summaryTitle = UIFactory.Label(overlay.transform, "", 20, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(summaryTitle, 28f, 0f);
            RectTransform scroll = UIFactory.ScrollList(overlay.transform, 0f);
            summaryBody = UIFactory.Label(scroll, "", 13);
            RectTransform row = UIFactory.Row(overlay.transform, 34f);
            summaryButton = UIFactory.Button(row, "Start next cycle", NextDay, Orange, 16);

            summaryOverlay.SetActive(false);
        }
    }
}
