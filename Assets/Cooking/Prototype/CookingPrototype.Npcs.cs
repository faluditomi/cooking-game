using System.Collections.Generic;
using System.Text;
using DialogueSystem;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// Regulars getting to know you: each customer's stage (Stranger → Familiar → Friendly), the
    /// dishes they like and order, and what they say. Lines come from the DialogueSystem catalog,
    /// triggered as (NPC, event, stage), and type out in the sidebar; when the catalog has nothing,
    /// the customer's fallback lines are used.
    /// </summary>
    /// <remarks>
    /// A visit: they arrive and order (Order). Their order dialogue says what they want, through the
    /// graph's payload (a dish name) or a [liked meal] / [dream dish] token in the text. While you
    /// cook they muse once (Musing) and hurry you now and then (Hurry). Serving plays their reaction
    /// (CorrectOrder, WrongOrderTasty, WrongOrderNasty, or DreamOrder once per game) as a priority
    /// trigger, so it plays straight after whatever they were saying.
    /// </remarks>
    public partial class CookingPrototype
    {
        [Header("Dialogue")]
        [Tooltip("Catalog the prototype checks for lines. Use the same one as the scene's DialogueDirector. Built by Custom Tools → Cooking Prototype → Build Dialogue.")]
        [SerializeField] private DialogueCatalog dialogueCatalog;
        [Tooltip("Letters per second in the sidebar.")]
        [SerializeField, Min(5f)] private float typewriterSpeed = 45f;

        private const int SpeechLines = 8;

        private readonly Dictionary<Customer, CustomerRelationship> relationships = new Dictionary<Customer, CustomerRelationship>();
        private readonly List<string> speech = new List<string>();
        private readonly List<string> progressToday = new List<string>();
        private readonly List<HistoryEntry> history = new List<HistoryEntry>();
        /// <summary>The current visit; lines and cooking steps are added to it.</summary>
        private HistoryEntry currentEntry;
        /// <summary>A desire clue waiting for the customer's reaction lines to finish.</summary>
        private string pendingClue;
        /// <summary>The dialogue event whose end releases <see cref="pendingClue"/>; None until the reaction is triggered.</summary>
        private DialogueEventType clueAfter;
        private float visitStarted;
        private float orderInAt;
        private float nextHurryAt;
        private float musingAt;
        private bool mused;
        /// <summary>The liked meal this visit's [liked meal] tokens stand for.</summary>
        private RecipeDefinition visitLiked;
        /// <summary>Letters of the newest line shown so far.</summary>
        private float typed;
        private Text resultSpeech;
        private Text sidebarCustomer;
        private Text sidebarSpeech;

        private void InitNpcs()
        {
            foreach(Customer regular in customers) Relationship(regular);

            // Play-once bags are session state on the catalog asset; start every session fresh.
            if(dialogueCatalog != null) dialogueCatalog.ResetPlayed();
        }

        private void OnEnable()
        {
            Dialogue.LineReceived += HandleDialogueLine;
            Dialogue.DialogueEnded += HandleDialogueEnded;
        }

        private void OnDisable()
        {
            Dialogue.LineReceived -= HandleDialogueLine;
            Dialogue.DialogueEnded -= HandleDialogueEnded;
        }

        private CustomerRelationship Relationship(Customer regular)
        {
            if(regular == null) return null;

            if(!relationships.TryGetValue(regular, out CustomerRelationship relationship))
            {
                relationship = new CustomerRelationship(regular);
                relationships[regular] = relationship;
            }

            return relationship;
        }

        private static string StageLabel(NpcStage stage)
        {
            switch(stage)
            {
                case NpcStage.Familiar: return "familiar";
                case NpcStage.Friendly: return "friendly";
                default: return "stranger";
            }
        }

        // ------------------------------------------------------------------ Speaking

        /// <summary>
        /// Has the current customer say something about <paramref name="eventType"/>: catalog lines
        /// for their stage, or any-stage lines, or <paramref name="fallback"/> if there are none.
        /// Returns true when the catalog will play lines.
        /// </summary>
        private bool Speak(DialogueEventType eventType, string fallback, bool priority = false)
        {
            if(customer == null) return false;

            NpcStage stage = Relationship(customer).Stage;
            DialogueNpcState state = DialogueNpcState.None;
            bool canPlay = DialogueDirector.Instance != null
                           && CustomerDialogue.TryResolve(dialogueCatalog, customer.dialogueId, eventType, stage, out state);

            if(canPlay)
            {
                // A pending clue follows when these lines end (HandleDialogueEnded).
                if(pendingClue != null) clueAfter = eventType;
                Dialogue.Trigger(customer.dialogueId, eventType, state, priority);
                return true;
            }

            if(!string.IsNullOrEmpty(fallback)) AddSpeech(fallback, SpeechTag(eventType));
            FlushClue();
            return false;
        }

        /// <summary>How a line is labelled in the history.</summary>
        private static string SpeechTag(DialogueEventType eventType)
        {
            switch(eventType)
            {
                case DialogueEventType.Order: return "Order";
                case DialogueEventType.Hurry: return "Hurrying";
                case DialogueEventType.Musing: return "Musing";
                case DialogueEventType.None: return "Says";
                default: return "Reaction";
            }
        }

        private void HandleDialogueLine(DialogueLineEventArgs line)
        {
            // Lines from a customer who already left don't belong to this visit.
            if(customer == null || line.TriggerNpc != customer.dialogueId) return;

            // The first order line says what they want.
            if(line.EventType == DialogueEventType.Order && !orderIn)
            {
                ResolveOrder(line.Graph != null ? line.Graph.payload : null, line.Text);
            }

            string text = CustomerDialogue.FillTokens(line.Text, NameIfSaved(visitLiked), NameIfSaved(Relationship(customer).DreamMeal));

            if(line.EventType == DialogueEventType.Order)
            {
                orderLine = string.IsNullOrEmpty(orderLine) ? text : orderLine + " " + text;
                if(currentEntry != null) currentEntry.OrderLine = orderLine;
                RefreshTickets();
            }

            AddSpeech(text, SpeechTag(line.EventType));
        }

        private void HandleDialogueEnded(DialogueEndedEventArgs ended)
        {
            if(customer == null || ended.TriggerNpc != customer.dialogueId) return;

            // An order dialogue without lines still has to end in an order.
            if(ended.EventType == DialogueEventType.Order && !orderIn) SetOrder(DefaultOrder());

            if(clueAfter == DialogueEventType.None || ended.EventType != clueAfter) return;
            FlushClue();
        }

        private void FlushClue()
        {
            if(string.IsNullOrEmpty(pendingClue)) return;

            string clue = pendingClue;
            pendingClue = null;
            clueAfter = DialogueEventType.None;
            AddSpeech(clue, "Clue");
        }

        /// <summary>A dish's name for their lines, or null when it isn't on the menu (unsaved or deleted).</summary>
        private string NameIfSaved(RecipeDefinition recipe)
        {
            return IsOnMenu(recipe) ? recipe.DisplayName : null;
        }

        private RecipeDefinition DefaultOrder()
        {
            return Relationship(customer).PickOrder(random, VisibleMenu());
        }

        /// <summary>Works out the order from the order dialogue's payload or tokens.</summary>
        private void ResolveOrder(string payload, string text)
        {
            CustomerRelationship relationship = Relationship(customer);
            RecipeDefinition pick = null;

            switch(CustomerDialogue.ReadOrder(payload, text, out string dishName))
            {
                case CustomerDialogue.OrderKind.Named:
                    pick = CustomerDialogue.FindByName(menu, dishName);
                    if(pick == null) Debug.LogWarning("[Cooking] Order dialogue names \"" + dishName + "\", which isn't a dish. Using a default order.");
                    break;
                case CustomerDialogue.OrderKind.Liked:
                    pick = visitLiked;
                    break;
                case CustomerDialogue.OrderKind.Dream:
                    pick = relationship.DreamMeal ?? visitLiked;
                    break;
            }

            SetOrder(pick ?? DefaultOrder());
        }

        /// <summary>Seconds since the current customer arrived.</summary>
        private float VisitTime => Time.time - visitStarted;

        private void AddSpeech(string text, string tag = "Says")
        {
            if(currentEntry != null) currentEntry.Speech.Add(new VisitEvent { Time = VisitTime, Tag = tag, Text = text });
            speech.Add(text);
            while(speech.Count > SpeechLines) speech.RemoveAt(0);
            typed = 0f;
            RefreshSpeech();
        }

        private void ClearSpeech()
        {
            speech.Clear();
            typed = 0f;
            RefreshSpeech();
        }

        /// <summary>Types the newest line out, letter by letter.</summary>
        private void TickSpeech(float deltaTime)
        {
            if(speech.Count == 0) return;

            int length = speech[speech.Count - 1].Length;
            if(typed >= length) return;

            int before = Mathf.FloorToInt(typed);
            typed = Mathf.Min(length, typed + deltaTime * typewriterSpeed);
            if(Mathf.FloorToInt(typed) != before) RefreshSpeech();
        }

        private void RefreshSpeech()
        {
            StringBuilder sb = new StringBuilder();

            for(int i = 0; i < speech.Count; i++)
            {
                bool newest = i == speech.Count - 1;
                string line = newest ? speech[i].Substring(0, Mathf.Clamp(Mathf.FloorToInt(typed), 0, speech[i].Length)) : speech[i];

                // Older lines fade so the newest reads first.
                sb.Append(newest ? "<color=#ebe6db>\"" : "<color=#8f8a83>\"").Append(line);
                if(!newest || typed >= speech[i].Length) sb.Append('"');
                sb.Append("</color>\n\n");
            }

            string text = sb.ToString();
            if(sidebarSpeech != null) sidebarSpeech.text = text;
            if(resultSpeech != null) resultSpeech.text = text;
        }

        private void RefreshSidebar()
        {
            if(sidebarCustomer == null) return;

            if(phase == Phase.Service && customer != null)
            {
                sidebarCustomer.text = customer.name + "\n<size=16><color=#a6a199>" + StageLabel(Relationship(customer).Stage)
                                       + (orderIn ? " · wants " + DishName(order) : " · ordering") + "</color></size>";
            }
            else if(phase == Phase.Service)
            {
                sidebarCustomer.text = "<color=#a6a199>Nobody at the counter</color>";
            }
            else
            {
                sidebarCustomer.text = "<color=#a6a199>" + (phase == Phase.Morning ? "Customers come when the day starts." : "Closed.") + "</color>";
            }
        }

        // ------------------------------------------------------------------ Visits

        /// <summary>A customer arrives: start a fresh visit record and have them order.</summary>
        private void BeginVisit()
        {
            pendingClue = null;
            clueAfter = DialogueEventType.None;
            visitStarted = Time.time;
            orderInAt = 0f;
            nextHurryAt = Time.time + Mathf.Max(5f, customer.hurryAfter);
            musingAt = float.MaxValue;
            mused = false;
            ClearSpeech();

            CustomerRelationship relationship = Relationship(customer);
            visitLiked = relationship.PickLiked(random);
            currentEntry = new HistoryEntry
            {
                Day = day,
                Visit = servedToday + 1,
                Customer = customer,
                Stage = relationship.Stage,
            };

            // Their order dialogue says what they want; without one, they just order.
            if(!Speak(DialogueEventType.Order, null))
            {
                SetOrder(DefaultOrder());
                orderLine = customer.GetOrderLine(order, random);
                currentEntry.OrderLine = orderLine;
                AddSpeech(orderLine, "Order");
                RefreshTickets();
            }
        }

        /// <summary>Per frame while a customer waits: they muse once, and hurry you every so often.</summary>
        private void TickVisit()
        {
            if(customer == null) return;

            if(orderIn && !mused && Time.time >= musingAt)
            {
                mused = true;
                Speak(DialogueEventType.Musing, null);
            }

            if(Time.time >= nextHurryAt)
            {
                nextHurryAt = Time.time + Mathf.Max(5f, customer.hurryAfter);
                Speak(DialogueEventType.Hurry, null);
            }
        }

        /// <summary>The order is known: note it for the history and start the musing countdown.</summary>
        private void OnOrderIn()
        {
            orderInAt = VisitTime;
            float min = Mathf.Max(0f, Mathf.Min(customer.musingAfter.x, customer.musingAfter.y));
            float max = Mathf.Max(customer.musingAfter.x, customer.musingAfter.y);
            musingAt = Time.time + Random.Range(min, max);

            if(currentEntry == null) return;
            currentEntry.Order = order;
            currentEntry.Ordered = DishName(order);
        }

        /// <summary>Notes a cooking step for the current visit's history.</summary>
        private void LogCooking(string tag, string text)
        {
            if(currentEntry == null || phase != Phase.Service || customer == null) return;
            currentEntry.Cooking.Add(new VisitEvent { Time = VisitTime, Tag = tag, Text = text });
        }

        /// <summary>
        /// Records the served dish with the customer: their relationship, the visit's history entry
        /// (with the dish, named or not), and (once they're familiar) a clue towards their secret
        /// desire. Call before the plate is cleared.
        /// </summary>
        private RelationshipChange RecordServe(JudgeResult result)
        {
            CustomerRelationship relationship = Relationship(customer);
            NpcStage stageBefore = relationship.Stage;
            List<RecipeDefinition> known = KnownRecipes();
            RelationshipChange change = relationship.Record(result, plate, known);

            if(currentEntry == null) currentEntry = new HistoryEntry { Day = day, Visit = servedToday, Customer = customer };

            // The dish as a recipe: a menu dish it makes exactly, a dish they already know, or a new nameless one.
            known = KnownRecipes();
            RecipeDefinition dish = CustomerRelationship.FindMatch(known, plate);
            foreach(HistoryEntry past in history) if(dish == null && past.Dish != null && CustomerRelationship.FindMatch(new[] { past.Dish }, plate) != null) dish = past.Dish;
            if(dish == null && plate.Count > 0) dish = CustomerRelationship.CreateMeal(CustomerRelationship.Nameless, plate);

            currentEntry.Stage = stageBefore;
            currentEntry.Order = order;
            currentEntry.Ordered = DishName(order);
            currentEntry.Dish = dish;
            currentEntry.OrderedFlavour = result.Ordered;
            currentEntry.OrderKnown = orderKnown;
            currentEntry.Flavour = result.Dish;
            currentEntry.BestZone = result.BestZone.IsMatch ? result.BestZone.Zone.DisplayName : null;
            currentEntry.Verdict = result.Verdict;
            currentEntry.Exact = result.ExactMatch;
            currentEntry.Similarity = result.Similarity;
            currentEntry.Price = result.Price;
            currentEntry.PayShare = result.PayShare;
            currentEntry.Payment = result.Payment;
            currentEntry.Tip = result.Tip;
            currentEntry.IngredientCost = dishCost;
            currentEntry.TimeTaken = Mathf.Max(0f, VisitTime - orderInAt);

            foreach(IngredientPortion portion in plate)
            {
                currentEntry.Served.Add(new IngredientPortion(portion.ingredient, portion.quantity, portion.prep, portion.cooking, portion.doneness));
            }

            LogCooking("Serve", "Served the plate.");
            history.Add(currentEntry);

            if(customer.secretDesire != null)
            {
                currentEntry.DesireDistance = result.Verdict == Verdict.DreamMeal ? 0f : customer.secretDesire.DistanceOutside(result.Dish);
                currentEntry.Warmth = relationship.RecordDesireDistance(currentEntry.DesireDistance);

                // Strangers keep it to themselves; once familiar they let things slip.
                if(result.Verdict != Verdict.DreamMeal && stageBefore != NpcStage.Stranger && relationship.DreamMeal == null)
                {
                    currentEntry.Clue = DesireClue.Describe(result.Dish, customer.secretDesire, currentEntry.Warmth, missingNeed: result.DesireScore >= 1f);
                    pendingClue = currentEntry.Clue;
                }
            }

            if(change.StageChanged)
            {
                progressToday.Add(change.After == NpcStage.Friendly
                    ? customer.name + " had their dream meal. You're friends now."
                    : customer.name + " is getting familiar with you.");
            }

            if(change.NewLikedMeal != null) progressToday.Add(customer.name + " liked something new: " + DishName(change.NewLikedMeal) + ".");
            return change;
        }

        /// <summary>Lines for the result screen about how the relationship moved.</summary>
        private string DescribeChange(RelationshipChange change)
        {
            StringBuilder sb = new StringBuilder();

            if(change.StageChanged)
            {
                sb.Append("<color=#f28c33><b>")
                  .Append(change.After == NpcStage.Friendly ? customer.name + " found their dream meal. You're friends now." : customer.name + " is getting familiar with you.")
                  .Append("</b></color>\n");
            }

            if(change.FirstTaste)
            {
                sb.Append("<color=#73cc73>Real food at last. They'll forget the raw stuff and order this instead.</color>\n");
            }
            else if(change.NewLikedMeal != null)
            {
                sb.Append("<color=#73cc73>They liked it and will order it again.</color>\n");
            }

            CustomerRelationship relationship = Relationship(customer);
            if(relationship.Stage == NpcStage.Stranger)
            {
                int left = Mathf.Max(0, customer.likedMealsToFamiliar - relationship.LikedCount);
                sb.Append("<color=#a6a199>").Append(left).Append(left == 1 ? " more liked meal" : " more liked meals").Append(" until they're familiar.</color>\n");
            }

            return sb.ToString();
        }

        private string DescribeProgressToday()
        {
            if(progressToday.Count == 0) return "";

            StringBuilder sb = new StringBuilder("<b>Regulars</b>\n");
            foreach(string note in progressToday) sb.Append("  <color=#f28c33>").Append(note).Append("</color>\n");
            return sb.Append('\n').ToString();
        }
    }
}
