using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Playable day loop on top of the flavour system.
    /// <list type="number">
    /// <item><b>Morning</b>: buy the day's ingredients in the <b>Store</b> and prep them on the
    /// <b>Prep</b> board. Nothing is timed.</item>
    /// <item><b>Service</b>: open the stall (the store closes). Customers come one at a time; taking
    /// an order starts its timer. Cook in the <b>Kitchen</b> from your stock. You can still prep, but
    /// that time comes off the timer. Late dishes are paid but never tipped, and the customer isn't happy.</item>
    /// <item><b>End of day</b>: after <see cref="customersPerDay"/> customers, a summary. Prepped
    /// leftovers spoil; raw stock keeps.</item>
    /// </list>
    /// The whole UI is built in code at startup, so the scene only needs this component. The three
    /// areas live in the partial files CookingPrototype.Store/Prep/Kitchen.cs.
    /// </summary>
    public partial class CookingPrototype : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField] private FlavourRules rules;
        [SerializeField] private List<FlavourZone> zones = new List<FlavourZone>();
        [SerializeField] private List<IngredientDefinition> pantry = new List<IngredientDefinition>();
        [SerializeField] private List<RecipeDefinition> menu = new List<RecipeDefinition>();
        [SerializeField] private List<Customer> customers = new List<Customer>();

        [Header("Day")]
        [SerializeField, Min(0f)] private float startingMoney = 60f;
        [SerializeField, Min(1)] private int customersPerDay = 5;

        [Header("Orders")]
        [Tooltip("Menu price = ingredient cost × margin.")]
        [SerializeField, Min(0f)] private float margin = 1.6f;
        [Tooltip("How well an unordered dish must share a zone with the order to be accepted.")]
        [SerializeField, Range(0f, 1f)] private float likeThreshold = 0.5f;
        [Tooltip("Off: orders have no timer and nothing is ever late (the calmer 'social kitchen' version). Can also be switched in game each morning.")]
        [SerializeField] private bool timedOrders = true;
        [Tooltip("Order timer = base + per unit of ingredient + per prep step the recipe needs.")]
        [SerializeField, Min(5f)] private float orderBaseSeconds = 30f;
        [SerializeField, Min(0f)] private float orderSecondsPerUnit = 4f;
        [SerializeField, Min(0f)] private float orderSecondsPerPrepStep = 3f;

        [Header("Stations")]
        [SerializeField, Min(1f)] private float wokSeconds = 8f;
        [SerializeField, Min(1f)] private float potSeconds = 10f;
        [Tooltip("How far each wok shake (one change of direction) knocks the bar back.")]
        [SerializeField, Range(0.01f, 0.3f)] private float shakeKnockback = 0.06f;

        [Header("Prep minigames (strokes per unit)")]
        [SerializeField, Min(1)] private int sliceStrokes = 6;
        [SerializeField, Min(1)] private int peelStrokes = 3;
        [SerializeField, Min(1)] private int grateStrokes = 5;

        [Header("Debug")]
        [Tooltip("Show the plate's flavour on the radar while cooking. The design reveals it only after serving; this is for tuning.")]
        [SerializeField] private bool livePreview = true;
        [Tooltip("0 picks a random seed.")]
        [SerializeField] private int randomSeed;

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
            public Text Speech;
            public Text Order;
            public Text Timer;
            public GameObject TimerBar;
            public RectTransform TimerFill;
            public Image TimerFillImage;
            public Button TakeOrder;
            public Button TimerToggle;
        }

        // Day state
        private System.Random random;
        private float money;
        private int day = 1;
        private Phase phase;
        private Area area;
        private readonly Inventory inventory = new Inventory();
        private bool overlayOpen;
        private int servedToday;
        private int happyToday;
        private float spentToday;
        private float earnedToday;

        // Current customer
        private int customerIndex = -1;
        private Customer customer;
        private RecipeDefinition order;
        private FlavourProfile orderProfile;
        private FlavourZone orderZone;
        private string orderLine;
        private bool orderTaken;
        private float orderDuration;
        private float orderTimeLeft;
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
            random = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random();
            money = startingMoney;
            if(customers.Count == 0) customers.Add(new Customer());

            CreateKitchenState();
            EnsureEventSystem();
            BuildUI();
            InitNpcs();
            StartDay();
        }

        private void Update()
        {
            if(overlayOpen || phase != Phase.Service) return;

            // The kitchen keeps cooking and the clock keeps running whichever area you're in.
            TickKitchen(Time.deltaTime);
            if(orderTaken && timedOrders) orderTimeLeft -= Time.deltaTime;
            UpdateTicketTimers();
            CheckHurry();
        }

        // ------------------------------------------------------------------ Day flow

        private void StartDay()
        {
            phase = Phase.Morning;
            servedToday = 0;
            happyToday = 0;
            spentToday = 0f;
            earnedToday = 0f;
            progressToday.Clear();
            customer = null;
            order = null;
            orderTaken = false;
            overlayOpen = false;
            summaryOverlay.SetActive(false);
            resultOverlay.SetActive(false);

            ShowArea(Area.Store);
            Say("Day " + day + ". Fill the cart for " + customersPerDay + " customers and check out, prep what you can, then open the stall.");
        }

        private void OpenStall()
        {
            if(phase != Phase.Morning) return;

            if(!cart.IsEmpty)
            {
                Say("You still have " + cart.TotalUnits + " units in the cart. Buy them or clear the cart before opening.", UIFactory.Bad);
                ShowArea(Area.Store);
                return;
            }

            if(inventory.IsEmpty && boardUnit == null)
            {
                Say("You have nothing to cook with. Buy something first.", UIFactory.Bad);
                return;
            }

            phase = Phase.Service;
            ShowArea(Area.Kitchen);
            NextCustomer();
        }

        private void NextCustomer()
        {
            overlayOpen = false;
            resultOverlay.SetActive(false);
            dishCost = 0f;
            ClearPlate();

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
            // What they order grows as they like things; after their dream meal, it's always that.
            order = Relationship(customer).PickOrder(random, menu);
            BeginVisit();
            orderProfile = order != null ? order.CalculateProfile(rules) : FlavourProfile.Zero;
            orderZone = FlavourEvaluator.FindBestZone(orderProfile, zones, Falloff).Zone;
            orderLine = customer.GetOrderLine(order, random);
            orderTaken = false;

            Say(customer.name + " is at the counter (" + (servedToday + 1) + " of " + customersPerDay + "). Take their order.");
            RefreshAll();
        }

        private void TakeOrder()
        {
            if(phase != Phase.Service || customer == null || orderTaken) return;

            orderTaken = true;
            orderDuration = OrderSeconds(order);
            orderTimeLeft = orderDuration;
            Say(customer.name + ": \"" + orderLine + "\"" + (timedOrders ? "  You have " + FormatTime(orderDuration) + "." : ""));
            Speak(DialogueSystem.DialogueEventType.Order, null);
            RefreshAll();
        }

        /// <summary>Switches between timed orders and the untimed version. Mornings only, so a day is all one or the other.</summary>
        private void ToggleTimedOrders()
        {
            if(phase != Phase.Morning) return;

            timedOrders = !timedOrders;
            Say(timedOrders
                ? "Timed orders: each order has a timer, and late dishes get no tip."
                : "Untimed orders: take as long as you like. Nothing is ever late.");
            RefreshAll();
        }

        /// <summary>Time allowed for <paramref name="recipe"/>: more for more units and more prep.</summary>
        private float OrderSeconds(RecipeDefinition recipe)
        {
            if(recipe == null) return orderBaseSeconds;

            int units = 0;
            int prepSteps = 0;

            foreach(IngredientPortion portion in recipe.Portions)
            {
                if(portion == null || !portion.IsValid) continue;
                units += portion.quantity;
                prepSteps += portion.quantity * CountSteps(portion.prep);
            }

            return orderBaseSeconds + units * orderSecondsPerUnit + prepSteps * orderSecondsPerPrepStep;
        }

        private static int CountSteps(PrepSteps prep)
        {
            int count = 0;
            if((prep & PrepSteps.Peeled) != 0) count++;
            if((prep & PrepSteps.Sliced) != 0) count++;
            if((prep & PrepSteps.Grated) != 0) count++;
            return count;
        }

        /// <summary>Called by the kitchen's Serve button.</summary>
        private void FinishOrder(JudgeResult result)
        {
            money += result.Payment + result.Tip;
            earnedToday += result.Payment + result.Tip;
            servedToday++;
            if(result.IsHappy) happyToday++;

            RelationshipChange change = RecordServe(result);
            ShowResult(result, change);
            ClearPlate();
        }

        private void AfterResult()
        {
            if(servedToday >= customersPerDay) EndDay();
            else NextCustomer();
        }

        private void EndDay()
        {
            phase = Phase.DayOver;
            resultOverlay.SetActive(false);
            customer = null;
            orderTaken = false;

            ReturnBoardToStock();
            ClearKitchen();
            List<Inventory.Entry> spoiled = inventory.SpoilPrepped();

            StringBuilder sb = new StringBuilder();
            sb.Append("Served <b>").Append(servedToday).Append("</b> customers, <b>").Append(happyToday).Append("</b> left happy.\n\n");
            sb.Append("Earned <b>$").Append(earnedToday.ToString("0.00")).Append("</b>\n");
            sb.Append("Spent at the store <b>$").Append(spentToday.ToString("0.00")).Append("</b>\n");
            float net = earnedToday - spentToday;
            sb.Append("Net <b><color=#").Append(net >= 0f ? "73cc73" : "e5594d").Append(">$").Append(net.ToString("0.00")).Append("</color></b>\n\n");
            sb.Append(DescribeProgressToday());

            if(spoiled.Count > 0)
            {
                float lost = 0f;
                sb.Append("<b>Spoiled overnight</b> (prepped stock doesn't keep)\n");

                foreach(Inventory.Entry entry in spoiled)
                {
                    lost += entry.Ingredient.Price * entry.Quantity;
                    sb.Append("  ").Append(Describe(new IngredientPortion(entry.Ingredient, entry.Quantity, entry.Prep))).Append('\n');
                }

                sb.Append("<color=#e5594d>Worth $").Append(lost.ToString("0.00")).Append("</color>\n\n");
            }
            else
            {
                sb.Append("Nothing spoiled.\n\n");
            }

            int rawLeft = 0;
            foreach(Inventory.Entry entry in inventory.Entries()) rawLeft += entry.Quantity;
            sb.Append(rawLeft == 0 ? "The pantry is empty."
                : rawLeft == 1 ? "1 raw unit carries over to tomorrow."
                : rawLeft + " raw units carry over to tomorrow.");
            sb.Append("\n\nMoney: <b>$").Append(money.ToString("0.00")).Append("</b>");

            summaryTitle.text = "Day " + day + " is over";
            summaryBody.text = sb.ToString();
            UIFactory.SetButtonText(summaryButton, "Start day " + (day + 1));
            summaryOverlay.SetActive(true);
            overlayOpen = true;
            RefreshAll();
        }

        private void NextDay()
        {
            day++;
            StartDay();
        }

        // ------------------------------------------------------------------ Areas

        private bool IsAreaOpen(Area target)
        {
            switch(target)
            {
                case Area.Store: return phase == Phase.Morning;
                case Area.Prep: return phase != Phase.DayOver;
                case Area.Kitchen: return phase == Phase.Service;
                default: return false;
            }
        }

        private void ShowArea(Area target)
        {
            if(!IsAreaOpen(target))
            {
                Say(target == Area.Store ? "The store is closed until tomorrow morning." : "The kitchen opens when you open the stall.", UIFactory.Bad);
                return;
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
                : phase == Phase.Service ? "Service " + Mathf.Min(servedToday + 1, customersPerDay) + "/" + customersPerDay
                : "Closed";
            dayText.text = "Day " + day + "  ·  " + phaseLabel;

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
            RefreshRecipeBook();
        }

        // ------------------------------------------------------------------ Ticket

        private void RefreshTickets()
        {
            foreach(TicketView ticket in tickets)
            {
                ticket.TakeOrder.transform.parent.gameObject.SetActive(phase == Phase.Service && customer != null && !orderTaken);
                ticket.TimerBar.SetActive(timedOrders && phase == Phase.Service && orderTaken);
                ticket.Timer.gameObject.SetActive(timedOrders && phase == Phase.Service && orderTaken);
                ticket.TimerToggle.transform.parent.gameObject.SetActive(phase == Phase.Morning);
                UIFactory.SetButtonText(ticket.TimerToggle, timedOrders ? "Timed orders: on" : "Timed orders: off");

                if(phase == Phase.Morning)
                {
                    ticket.Customer.text = "No customers yet";
                    ticket.Line.text = "<color=#a6a199>Buy ingredients in the Store and prep what you can. Prepped stock spoils overnight; raw keeps. "
                                       + customersPerDay + " customers today, and the store closes when you open the stall.</color>";
                    ticket.Order.text = DescribeRegulars();
                }
                else if(phase == Phase.Service && customer != null)
                {
                    ticket.Customer.text = customer.name + "  <size=17><color=#a6a199>" + StageLabel(Relationship(customer).Stage) + "</color></size>";

                    if(orderTaken)
                    {
                        ticket.Line.text = "\"" + orderLine + "\"";
                        ticket.Order.text = DescribeRecipe(order);
                    }
                    else
                    {
                        ticket.Line.text = "<color=#a6a199>Waiting at the counter. The timer starts when you take the order.</color>";
                        ticket.Order.text = "";
                    }
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
            if(!orderTaken) return;

            float share = orderDuration > 0f ? Mathf.Clamp01(orderTimeLeft / orderDuration) : 0f;
            Color color = orderTimeLeft <= 0f ? UIFactory.Bad : share < 0.25f ? UIFactory.Accent : UIFactory.Good;
            string label = orderTimeLeft >= 0f ? FormatTime(orderTimeLeft) + " left" : "LATE +" + FormatTime(-orderTimeLeft);

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

                if(relationship.DreamMeal != null)
                {
                    sb.Append("always ").Append(relationship.DreamMeal.DisplayName).Append('\n');
                    continue;
                }

                if(relationship.LikedMeals.Count == 0)
                {
                    sb.Append("anything on the menu\n");
                    continue;
                }

                for(int i = 0; i < relationship.LikedMeals.Count; i++)
                {
                    if(i > 0) sb.Append(", ");
                    sb.Append(relationship.LikedMeals[i].DisplayName);
                }

                sb.Append('\n');
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

            StringBuilder sb = new StringBuilder();
            sb.Append("<size=24><b>").Append(recipe.DisplayName).Append("</b></size>\n");
            if(!string.IsNullOrEmpty(recipe.Origin)) sb.Append("<color=#a6a199>Origin: ").Append(recipe.Origin).Append("</color>\n");
            sb.Append('\n');

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
                sb.Append("• ").Append(Describe(portion, true)).Append("  ").Append(readiness).Append('\n');
            }

            sb.Append("<color=#a6a199>Steps are in the Recipe book.</color>\n");
            sb.Append("\nMenu price <b>$").Append(DishJudge.MenuPrice(recipe, margin).ToString("0.00")).Append("</b>");

            if(orderZone != null)
            {
                sb.Append("\nTastes like <color=#").Append(ColorUtility.ToHtmlStringRGB(orderZone.Color)).Append(">")
                  .Append(orderZone.DisplayName).Append("</color>");
            }

            return sb.ToString();
        }

        private void Say(string text, Color? color = null)
        {
            if(messageText == null) return;
            messageText.text = text;
            messageText.color = color ?? UIFactory.TextColor;
        }

        private static string ZoneText(FlavourZoneMatch match)
        {
            if(!match.IsMatch) return "<color=#a6a199>none</color>";
            return "<color=#" + ColorUtility.ToHtmlStringRGB(match.Zone.Color) + ">" + match.Zone.DisplayName + "</color> "
                   + (match.Score * 100f).ToString("0") + "%";
        }

        // ------------------------------------------------------------------ Result

        private void ShowResult(JudgeResult result, RelationshipChange change)
        {
            overlayOpen = true;
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

            if(result.Late && result.IsPaid && result.Verdict != Verdict.DreamMeal)
            {
                title = "Late. They paid, but they're not happy";
                line = "Took you long enough.";
                color = UIFactory.Accent;
            }

            resultTitle.text = title;
            resultTitle.color = color;

            // What they say comes from the dialogue catalog (lines stream in below the title),
            // or the customer's fallback line when the catalog has nothing for this moment.
            Speak(CustomerDialogue.EventFor(result), line);

            StringBuilder sb = new StringBuilder();
            sb.Append(DescribeChange(change));
            sb.Append("<b>Ordered</b>  ").Append(order != null ? order.DisplayName : "-").Append('\n');
            sb.Append("<b>Served</b>\n");
            foreach(IngredientPortion portion in plate) sb.Append("  ").Append(Describe(portion, true)).Append('\n');

            if(result.Late) sb.Append("\n<color=#e5594d>Served ").Append(FormatTime(-orderTimeLeft)).Append(" late: no tip.</color>\n");
            else if(timedOrders) sb.Append("\n<color=#73cc73>On time, with ").Append(FormatTime(orderTimeLeft)).Append(" to spare.</color>\n");

            sb.Append("\n<b>Flavour</b>\n<size=17>").Append(result.Dish.ToString()).Append("</size>\n\n");
            sb.Append("Exact order: ").Append(result.ExactMatch ? "<color=#73cc73>yes</color>" : "no").Append('\n');
            sb.Append("Best zone: ").Append(ZoneText(result.BestZone)).Append('\n');
            sb.Append("Same zone as the order: ").Append(ZoneText(result.SharedZone)).Append('\n');
            if(result.RefusedIngredient != null) sb.Append("Won't eat: <color=#e5594d>").Append(result.RefusedIngredient.DisplayName).Append("</color>\n");
            if(result.DesireScore >= 0f) sb.Append("<color=#a6a199>Secret desire (debug): ").Append((result.DesireScore * 100f).ToString("0")).Append("%</color>\n");

            float profit = result.Payment + result.Tip - dishCost;
            sb.Append("\nPaid <b>$").Append(result.Payment.ToString("0.00")).Append("</b>");
            if(result.Tip > 0f) sb.Append("  + tip <b>$").Append(result.Tip.ToString("0.00")).Append("</b>");
            sb.Append("\nIngredients used $").Append(dishCost.ToString("0.00"));
            sb.Append("\nProfit <b><color=#").Append(profit >= 0f ? "73cc73" : "e5594d").Append(">$").Append(profit.ToString("0.00")).Append("</color></b>");
            resultBody.text = sb.ToString();

            FlavourZone highlight = result.SharedZone.Zone != null ? result.SharedZone.Zone : result.BestZone.Zone;
            resultRadar.SetHighlight(highlight);
            resultRadar.ClearProfiles();
            resultRadar.AddProfile(result.Ordered, new Color(0.85f, 0.85f, 0.85f, 0.7f), false);
            resultRadar.AddProfile(result.Dish, UIFactory.Accent, true);

            UIFactory.SetButtonText(resultButton, servedToday >= customersPerDay ? "Close for the day" : "Next customer");
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

            RectTransform content = UIFactory.Rect(root, "Content");
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(0f, 44f);
            content.offsetMax = new Vector2(0f, -60f);

            areaRoots[Area.Store] = BuildStoreArea(AreaRoot(content, "Store Area"));
            areaRoots[Area.Prep] = BuildPrepArea(AreaRoot(content, "Prep Area"));
            areaRoots[Area.Kitchen] = BuildKitchenArea(AreaRoot(content, "Kitchen Area"));

            // The book sits over the areas; result and summary screens go on top of it.
            BuildRecipeBook(root);
            BuildResultOverlay(root);
            BuildSummaryOverlay(root);
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
            UIFactory.Place(title.rectTransform, 0f, 0f, 0.17f, 1f).offsetMin = new Vector2(20f, 0f);

            RectTransform tabRow = UIFactory.Rect(top, "Tabs");
            UIFactory.Place(tabRow, 0.17f, 0f, 0.47f, 1f, 8f);
            UIFactory.Horizontal(tabRow.gameObject, 8f);
            tabs[Area.Store] = UIFactory.Button(tabRow, "Store", () => ShowArea(Area.Store), null, 20);
            tabs[Area.Prep] = UIFactory.Button(tabRow, "Prep", () => ShowArea(Area.Prep), null, 20);
            tabs[Area.Kitchen] = UIFactory.Button(tabRow, "Kitchen", () => ShowArea(Area.Kitchen), null, 20);

            // Always available, whichever area you're in.
            recipeBookButton = UIFactory.Button(top, "Recipe book", ToggleRecipeBook, new Color(0.3f, 0.36f, 0.5f, 1f), 20);
            UIFactory.Place((RectTransform)recipeBookButton.transform, 0.47f, 0f, 0.59f, 1f, 8f);

            dayText = UIFactory.Label(top, "", 22, TextAnchor.MiddleCenter);
            UIFactory.Place(dayText.rectTransform, 0.59f, 0f, 0.72f, 1f);

            phaseButton = UIFactory.Button(top, "Open the stall →", OpenStall, new Color(0.75f, 0.42f, 0.15f, 1f), 20);
            UIFactory.Place((RectTransform)phaseButton.transform, 0.72f, 0f, 0.86f, 1f, 8f);

            moneyText = UIFactory.Label(top, "", 30, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);
            UIFactory.Place(moneyText.rectTransform, 0.86f, 0f, 1f, 1f).offsetMax = new Vector2(-20f, 0f);
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

        /// <summary>The customer / order ticket with its timer, filling <paramref name="column"/>.</summary>
        private void BuildTicket(RectTransform column)
        {
            VerticalLayoutGroup layout = UIFactory.Vertical(column.gameObject, 10f, 16);
            layout.childForceExpandHeight = false;

            TicketView ticket = new TicketView();
            Header(column, "CUSTOMER");
            ticket.Customer = UIFactory.Label(column, "", 30, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            ticket.Line = UIFactory.Label(column, "", 19, TextAnchor.UpperLeft, FontStyle.Italic);
            ticket.Speech = UIFactory.Label(column, "", 18, TextAnchor.UpperLeft, FontStyle.Italic);

            RectTransform row = UIFactory.Row(column, 50f);
            ticket.TakeOrder = UIFactory.Button(row, "Take order", TakeOrder, new Color(0.75f, 0.42f, 0.15f, 1f), 22);

            RectTransform toggleRow = UIFactory.Row(column, 40f);
            ticket.TimerToggle = UIFactory.Button(toggleRow, "", ToggleTimedOrders, null, 18);

            Image timerBack = UIFactory.Panel(column, "Timer", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(timerBack, 16f, 0f);
            ticket.TimerBar = timerBack.gameObject;
            ticket.TimerFillImage = UIFactory.Panel(timerBack.transform, "Fill", UIFactory.Good);
            ticket.TimerFill = ticket.TimerFillImage.rectTransform;
            UIFactory.Place(ticket.TimerFill, 0f, 0f, 1f, 1f);
            ticket.Timer = UIFactory.Label(column, "", 24, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(ticket.Timer, 30f, 0f);

            ticket.Order = UIFactory.Label(column, "", 18);
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
                Color color = entry.Prep == PrepSteps.None ? UIFactory.ButtonColor : new Color(0.24f, 0.3f, 0.24f, 1f);
                Button button = UIFactory.Button(content, label, () => onClick(captured), color, 17);
                Text text = button.GetComponentInChildren<Text>();
                text.alignment = TextAnchor.MiddleLeft;
                text.rectTransform.offsetMin = new Vector2(12f, 0f);
                button.interactable = interactable;
                UIFactory.Size(button, 38f, 0f);
            }
        }

        private void BuildResultOverlay(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Result Overlay", new Color(0f, 0f, 0f, 0.78f));
            UIFactory.Stretch(overlay.rectTransform);
            resultOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Result", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(1400f, 860f);

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

            resultTitle = UIFactory.Label(right, "", 34, TextAnchor.UpperLeft, FontStyle.Bold);
            resultSpeech = UIFactory.Label(right, "", 21, TextAnchor.UpperLeft, FontStyle.Italic);
            resultBody = UIFactory.Label(right, "", 19);
            UIFactory.Size(resultBody, -1f, 1f);
            RectTransform row = UIFactory.Row(right, 56f);
            resultButton = UIFactory.Button(row, "Next customer", AfterResult, new Color(0.75f, 0.42f, 0.15f, 1f), 24);

            resultOverlay.SetActive(false);
        }

        private void BuildSummaryOverlay(Transform root)
        {
            Image overlay = UIFactory.Panel(root, "Summary Overlay", new Color(0f, 0f, 0f, 0.82f));
            UIFactory.Stretch(overlay.rectTransform);
            summaryOverlay = overlay.gameObject;

            RectTransform panel = UIFactory.Panel(overlay.transform, "Summary", UIFactory.PanelColor).rectTransform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(760f, 760f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 16f, 36);
            layout.childForceExpandHeight = false;

            summaryTitle = UIFactory.Label(panel, "", 38, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            summaryBody = UIFactory.Label(panel, "", 21);
            UIFactory.Size(summaryBody, -1f, 1f);
            RectTransform row = UIFactory.Row(panel, 56f);
            summaryButton = UIFactory.Button(row, "Start next day", NextDay, new Color(0.75f, 0.42f, 0.15f, 1f), 24);

            summaryOverlay.SetActive(false);
        }
    }
}
