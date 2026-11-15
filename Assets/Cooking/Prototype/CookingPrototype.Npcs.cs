using System.Collections.Generic;
using System.Text;
using DialogueSystem;
using UnityEngine;

namespace Cooking.Prototype
{
    /// <summary>
    /// Regulars getting to know you: each customer's stage (Dejected → Warmed up → Eureka), the
    /// dishes they like and order, and what they say. Lines come from the DialogueSystem catalog,
    /// triggered as (NPC, event, stage); when the catalog has nothing, the customer's fallback
    /// lines are used.
    /// </summary>
    public partial class CookingPrototype
    {
        [Header("Dialogue")]
        [Tooltip("Catalog the prototype checks for lines. Use the same one as the scene's DialogueDirector. Built by Custom Tools → Cooking Prototype → Build Dialogue.")]
        [SerializeField] private DialogueCatalog dialogueCatalog;
        [Tooltip("Share of the order timer left when the customer starts hurrying you.")]
        [SerializeField, Range(0f, 1f)] private float hurryAt = 0.25f;

        private const int SpeechLines = 4;

        private readonly Dictionary<Customer, CustomerRelationship> relationships = new Dictionary<Customer, CustomerRelationship>();
        private readonly List<string> speech = new List<string>();
        private readonly List<string> progressToday = new List<string>();
        private readonly List<HistoryEntry> history = new List<HistoryEntry>();
        /// <summary>The served dish of the current visit; later lines are added to it.</summary>
        private HistoryEntry currentEntry;
        /// <summary>A desire clue waiting for the customer's reaction lines to finish.</summary>
        private string pendingClue;
        /// <summary>The dialogue event whose end releases <see cref="pendingClue"/>; None until the reaction is triggered.</summary>
        private DialogueEventType clueAfter;
        private float visitStarted;
        private float orderTakenAt;
        private bool hurried;
        private UnityEngine.UI.Text resultSpeech;

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
                case NpcStage.WarmedUp: return "warmed up";
                case NpcStage.Eureka: return "eureka";
                default: return "dejected";
            }
        }

        // ------------------------------------------------------------------ Speaking

        /// <summary>
        /// Has the current customer say something about <paramref name="eventType"/>: catalog lines
        /// for their stage, or any-stage lines, or <paramref name="fallback"/> if there are none.
        /// </summary>
        private void Speak(DialogueEventType eventType, string fallback)
        {
            if(customer == null) return;

            NpcStage stage = Relationship(customer).Stage;
            DialogueNpcState state = DialogueNpcState.None;
            bool canPlay = DialogueDirector.Instance != null
                           && CustomerDialogue.TryResolve(dialogueCatalog, customer.dialogueId, eventType, stage, out state);

            if(canPlay)
            {
                // A pending clue follows when these lines end (HandleDialogueEnded), not when some
                // earlier dialogue (the order chat) is cut off by this one.
                if(pendingClue != null) clueAfter = eventType;
                Dialogue.Trigger(customer.dialogueId, eventType, state);
            }
            else
            {
                if(!string.IsNullOrEmpty(fallback)) AddSpeech(fallback, SpeechTag(eventType));
                FlushClue();
            }
        }

        /// <summary>How a line is labelled in the history.</summary>
        private static string SpeechTag(DialogueEventType eventType)
        {
            switch(eventType)
            {
                case DialogueEventType.Order: return "Order";
                case DialogueEventType.Hurry: return "Hurrying";
                case DialogueEventType.None: return "Says";
                default: return "Reaction";
            }
        }

        private void HandleDialogueLine(DialogueLineEventArgs line)
        {
            // Lines from a customer who already left (aborted by Replace) don't belong to this visit.
            if(customer == null || line.TriggerNpc != customer.dialogueId) return;
            AddSpeech(line.Text, SpeechTag(line.EventType));
        }

        private void HandleDialogueEnded(DialogueEndedEventArgs ended)
        {
            if(customer == null || ended.TriggerNpc != customer.dialogueId) return;
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

        /// <summary>Seconds since the current customer arrived.</summary>
        private float VisitTime => Time.time - visitStarted;

        private void AddSpeech(string text, string tag = "Says")
        {
            if(currentEntry != null) currentEntry.Speech.Add(new VisitEvent { Time = VisitTime, Tag = tag, Text = text });
            speech.Add(text);
            while(speech.Count > SpeechLines) speech.RemoveAt(0);
            RefreshSpeech();
        }

        private void ClearSpeech()
        {
            speech.Clear();
            RefreshSpeech();
        }

        private void RefreshSpeech()
        {
            StringBuilder sb = new StringBuilder();

            for(int i = 0; i < speech.Count; i++)
            {
                // Older lines fade so the newest reads first.
                bool newest = i == speech.Count - 1;
                sb.Append(newest ? "<color=#ebe6db>\"" : "<color=#8f8a83>\"").Append(speech[i]).Append("\"</color>\n");
            }

            string text = sb.ToString();
            foreach(TicketView ticket in tickets) ticket.Speech.text = text;
            if(resultSpeech != null) resultSpeech.text = text;
        }

        /// <summary>Per frame during an order: the customer hurries you once when time runs low.</summary>
        private void CheckHurry()
        {
            if(hurried || !timedOrders || !orderTaken || orderDuration <= 0f) return;
            if(orderTimeLeft > orderDuration * hurryAt) return;

            hurried = true;
            Speak(DialogueEventType.Hurry, null);
        }

        // ------------------------------------------------------------------ Visits

        /// <summary>A customer arrives: start a fresh visit record that collects what's said and cooked until they're served.</summary>
        private void BeginVisit()
        {
            hurried = false;
            pendingClue = null;
            clueAfter = DialogueEventType.None;
            visitStarted = Time.time;
            orderTakenAt = 0f;
            ClearSpeech();
            currentEntry = NewVisitEntry();
        }

        private HistoryEntry NewVisitEntry()
        {
            return new HistoryEntry
            {
                Day = day,
                Visit = servedToday + 1,
                Customer = customer,
                Stage = Relationship(customer).Stage,
                Ordered = order != null ? order.DisplayName : "-",
            };
        }

        /// <summary>Notes a cooking step for the current visit's history.</summary>
        private void LogCooking(string tag, string text)
        {
            if(currentEntry == null || phase != Phase.Service) return;
            currentEntry.Cooking.Add(new VisitEvent { Time = VisitTime, Tag = tag, Text = text });
        }

        private void OnOrderTaken()
        {
            orderTakenAt = VisitTime;
            if(currentEntry == null) return;

            currentEntry.OrderLine = orderLine;
            currentEntry.Timed = timedOrders;
            currentEntry.TimeAllowed = orderDuration;
            currentEntry.Speech.Add(new VisitEvent { Time = VisitTime, Tag = "Order", Text = orderLine });
        }

        /// <summary>
        /// Records the served dish with the customer: their relationship, the visit's history entry,
        /// and (once they've warmed up) a clue towards their secret desire. Call before the plate is cleared.
        /// </summary>
        private RelationshipChange RecordServe(JudgeResult result)
        {
            CustomerRelationship relationship = Relationship(customer);
            NpcStage stageBefore = relationship.Stage;
            RelationshipChange change = relationship.Record(result, plate);

            if(currentEntry == null) currentEntry = NewVisitEntry();

            currentEntry.Stage = stageBefore;
            currentEntry.Ordered = order != null ? order.DisplayName : "-";
            currentEntry.OrderedFlavour = result.Ordered;
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
            currentEntry.Late = result.Late;
            currentEntry.TimeTaken = Mathf.Max(0f, VisitTime - orderTakenAt);

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

                // Dejected regulars keep it to themselves; once warmed up they let things slip.
                if(result.Verdict != Verdict.DreamMeal && result.Verdict != Verdict.Refused && stageBefore != NpcStage.Dejected)
                {
                    currentEntry.Clue = DesireClue.Describe(result.Dish, customer.secretDesire, currentEntry.Warmth);
                    pendingClue = currentEntry.Clue;
                }
            }

            if(change.StageChanged)
            {
                progressToday.Add(change.After == NpcStage.Eureka
                    ? customer.name + " had their dream meal. They'll order it from now on."
                    : customer.name + " warmed up to you.");
            }

            if(change.NewLikedMeal != null) progressToday.Add(customer.name + " liked something new: " + change.NewLikedMeal.DisplayName + ".");
            return change;
        }

        /// <summary>Lines for the result screen about how the relationship moved.</summary>
        private string DescribeChange(RelationshipChange change)
        {
            StringBuilder sb = new StringBuilder();

            if(change.StageChanged)
            {
                sb.Append("<color=#f28c33><b>")
                  .Append(change.After == NpcStage.Eureka ? "Eureka! " + customer.name + " found their dream meal." : customer.name + " is warming up to you.")
                  .Append("</b></color>\n");
            }

            if(change.NewLikedMeal != null)
            {
                sb.Append("<color=#73cc73>They'll order this again as \"").Append(change.NewLikedMeal.DisplayName).Append("\".</color>\n");
            }

            CustomerRelationship relationship = Relationship(customer);
            if(relationship.Stage == NpcStage.Dejected)
            {
                int left = Mathf.Max(0, customer.happyMealsToWarmUp - relationship.HappyMeals);
                sb.Append("<color=#a6a199>").Append(left).Append(left == 1 ? " more happy meal" : " more happy meals").Append(" until they open up.</color>\n");
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
