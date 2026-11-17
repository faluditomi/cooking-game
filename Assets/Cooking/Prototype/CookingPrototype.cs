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
            ApplySkin();
            BuildUI();
            InitNpcs();
            StartDay();
            ShowTitle();
        }

        private void Update()
        {
            // Lines keep typing out on the result screen too.
            TickSpeech(Time.deltaTime);
            // Whatever is held follows the pointer, and drops when the button comes up.
            UpdateHeld();

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

            ShuffleBillboards();
            ShowTabletPage(TabletPage.Shop);
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
                    ShowTabletPage(TabletPage.Shop);
                    return;
                }

                spentToday += total;
                Say("Bought " + units + " units for $" + total.ToString("0.00") + ".", UIFactory.Good);
            }

            if(inventory.IsEmpty && !DeskHasIngredients())
            {
                Say("You have nothing to cook with. Put something in the cart first.", UIFactory.Bad);
                ShowTabletPage(TabletPage.Shop);
                return;
            }

            phase = Phase.Service;
            // The tablet goes back into the workbench, showing the order.
            tabletPage = TabletPage.Order;
            tabletBig = false;
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
            tabletPage = TabletPage.Order;
            tabletBig = false;

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

            ReturnDeskToStock();
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
            ReturnDeskToStock();
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

        private void RefreshAll()
        {
            moneyText.text = "$" + money.ToString("0.00");

            string phaseLabel = phase == Phase.Morning ? "Morning"
                : phase == Phase.Service ? (dayTimer ? FormatTime(dayTimeLeft) + " left" : "Open")
                : "Closed";
            dayText.text = "Cycle " + day + "  ·  " + phaseLabel;

            phaseButton.interactable = phase == Phase.Morning;

            RefreshTablet();
            RefreshStore();
            RefreshKitchen();
            RefreshTickets();
            RefreshSidebar();
            RefreshMarket();
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
                    ticket.Line.text = "<color=#a6a199>Fill the cart in the Shop. Prepped stock spoils overnight; raw keeps. "
                                       + "Each regular comes once today. Rent at the end of the cycle: $" + stallRent.ToString("0.00") + ".</color>";
                    ticket.Order.text = DescribeRegulars();
                }
                else if(phase == Phase.Service && customer != null)
                {
                    ticket.Customer.text = customer.name + "  <size=17><color=#a6a199>" + StageLabel(Relationship(customer).Stage) + "</color></size>";

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
                int ready = inventory.Count(portion.ingredient, portion.prep) + OnDesk(portion.ingredient, portion.prep);
                int total = inventory.Total(portion.ingredient) + OnDesk(portion.ingredient);
                string readiness = ready >= portion.quantity ? "<color=#73cc73>ready</color>"
                    : total >= portion.quantity ? "<color=#f28c33>needs prep</color>"
                    : "<color=#e5594d>short</color>";
                // Prep only: how it's cooked is the dish type above, and repeating it wraps the narrow ticket.
                sb.Append("- ").Append(Describe(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep))).Append("  ").Append(readiness).Append('\n');
            }

            sb.Append("<color=#a6a199>Steps: Recipes tab.</color>\n");
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
            tabletPage = TabletPage.Order;
            tabletBig = false;
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

            if(change.FirstTaste)
            {
                title = "They've never tasted anything like it. They liked it";
                line = customer.likedLine;
                color = UIFactory.Good;
            }
            else if(result.PaidAnyway)
            {
                title = "They can't tell good food from bad yet. They pay anyway";
                color = UIFactory.Accent;
            }

            resultTitle.text = title;
            resultTitle.color = color;

            // What they say comes from the dialogue catalog (it plays right after anything they were
            // saying), or the customer's fallback line when the catalog has nothing for this moment.
            DialogueSystem.DialogueEventType reaction = change.FirstTaste ? DialogueSystem.DialogueEventType.CorrectOrder
                : CustomerDialogue.EventFor(result, change.FoundDream || result.Verdict != Verdict.DreamMeal);
            Speak(reaction, line, priority: true);

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

        /// <summary>The Dish Served page, on the big tablet.</summary>
        private void BuildResultOverlay(Transform pages)
        {
            Image overlay = UIFactory.Panel(pages, "Dish Served", UIFactory.PanelColor);
            UIFactory.Stretch(overlay.rectTransform);
            resultOverlay = overlay.gameObject;
            RectTransform panel = overlay.rectTransform;

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

        /// <summary>The End of Cycle page, on the big tablet.</summary>
        private void BuildSummaryOverlay(Transform pages)
        {
            Image overlay = UIFactory.Panel(pages, "End of Cycle", UIFactory.Background);
            UIFactory.Stretch(overlay.rectTransform);
            summaryOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Summary", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = new Vector2(0.5f, 0f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.sizeDelta = new Vector2(900f, -24f);
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
