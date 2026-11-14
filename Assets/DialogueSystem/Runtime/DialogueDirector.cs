using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    /// <summary>
    /// A queued trigger: the NPC grouping key plus the event type and NPC state used to match
    /// catalog entries.
    /// </summary>
    public readonly struct DialogueEvent
    {
        /// <summary>The NPC that raised the trigger.</summary>
        public readonly DialogueNpcId Npc;

        /// <summary>Event type used to match catalog entries.</summary>
        public readonly DialogueEventType EventType;

        /// <summary>NPC state used to match catalog entries.</summary>
        public readonly DialogueNpcState NpcState;

        public DialogueEvent(DialogueNpcId npc, DialogueEventType eventType, DialogueNpcState npcState)
        {
            Npc = npc;
            EventType = eventType;
            NpcState = npcState;
        }
    }

    /// <summary>
    /// Scene entry point. Owns the single <see cref="DialogueRunner"/>, the deferred trigger
    /// queue, and the policy for triggers that arrive mid-dialogue. All starting and resolving
    /// happens here in Update, never inside runner callbacks.
    /// </summary>
    [AddComponentMenu("Dialogue System/Dialogue Director")]
    public class DialogueDirector : MonoBehaviour
    {
        // A dialogue raising the trigger that started it would otherwise loop forever in one frame.
        private const int MaxTriggersPerUpdate = 256;

        [Header("Content")]
        [SerializeField] private DialogueCatalog catalog;

        [Header("Timing")]
        [Tooltip("Used for lines whose delay is negative.")]
        [SerializeField] private float defaultLineDelaySeconds = 1.2f;

        [Header("Behaviour")]
        [Tooltip("What happens when a trigger with a matching catalog group arrives while a dialogue is playing.")]
        [SerializeField] private DialoguePolicyOnNewTrigger policyOnNewTrigger = DialoguePolicyOnNewTrigger.Queue;
        [Tooltip("Keep this director alive across scene loads.")]
        [SerializeField] private bool persistAcrossScenes = true;
        [SerializeField] private bool verboseLogging = false;

        public static DialogueDirector Instance { get; private set; }

        public bool IsActive => runner != null && runner.IsActive;

        /// <summary>The NPC whose dialogue is currently playing, or <see cref="DialogueNpcId.None"/> when idle.</summary>
        public DialogueNpcId ActiveTrigger => IsActive ? runner.TriggerNpc : DialogueNpcId.None;

        private DialogueRunner runner;
        private readonly Queue<DialogueEvent> pendingTriggers = new Queue<DialogueEvent>();
        private readonly Queue<DialogueEvent> waitingDialogues = new Queue<DialogueEvent>();

        private void Awake()
        {
            if(Instance != null && Instance != this)
            {
                Debug.LogWarning($"[DialogueSystem] Duplicate DialogueDirector on '{name}' destroyed; only one may exist.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if(persistAcrossScenes) DontDestroyOnLoad(gameObject);
            Dialogue.Bind(this);

            runner = new DialogueRunner();
            runner.DefaultLineDelaySeconds = defaultLineDelaySeconds;
            runner.LineDelivered += HandleLineDelivered;
            runner.Finished += HandleFinished;
            runner.Aborted += HandleAborted;
        }

        private void OnDestroy()
        {
            if(Instance != this) return;

            Instance = null;
            Dialogue.Unbind(this);

            if(runner != null)
            {
                runner.LineDelivered -= HandleLineDelivered;
                runner.Finished -= HandleFinished;
                runner.Aborted -= HandleAborted;
                runner = null;
            }
        }

        // Per-frame order: runner tick -> triggers.
        private void Update()
        {
            // A duplicate director is destroyed in Awake but may still receive one Update
            // before the deferred destruction lands.
            if(runner == null) return;

            runner.Tick(Time.deltaTime);
            DrainTriggers();
        }

        /// <summary>Queues the trigger for the next drain.</summary>
        internal void EnqueueTrigger(DialogueEvent triggerEvent)
        {
            if(triggerEvent.Npc == DialogueNpcId.None)
            {
                Debug.LogWarning("[DialogueSystem] EnqueueTrigger called with NPC None; ignored.");
                return;
            }

            pendingTriggers.Enqueue(triggerEvent);
        }

        private void DrainTriggers()
        {
            int processed = 0;

            while(pendingTriggers.Count > 0)
            {
                if(++processed > MaxTriggersPerUpdate)
                {
                    Debug.LogWarning($"[DialogueSystem] More than {MaxTriggersPerUpdate} triggers drained in one frame; dropping {pendingTriggers.Count} remaining trigger(s). A dialogue probably raises the trigger that started it.");
                    pendingTriggers.Clear();
                    break;
                }

                ProcessTrigger(pendingTriggers.Dequeue());
            }

            TryStartQueued();
        }

        private void ProcessTrigger(DialogueEvent triggerEvent)
        {
            if(runner.IsActive)
            {
                HandleTriggerWhileActive(triggerEvent);
                return;
            }

            // Queued dialogues have priority over a fresh trigger while idle.
            TryStartQueued();

            if(runner.IsActive)
            {
                HandleTriggerWhileActive(triggerEvent);
                return;
            }

            if(catalog != null && catalog.HasRule(triggerEvent.Npc, triggerEvent.EventType, triggerEvent.NpcState)) StartTrigger(triggerEvent);
        }

        private void HandleTriggerWhileActive(DialogueEvent triggerEvent)
        {
            if(catalog == null || !catalog.HasRule(triggerEvent.Npc, triggerEvent.EventType, triggerEvent.NpcState)) return;

            switch(policyOnNewTrigger)
            {
                case DialoguePolicyOnNewTrigger.Ignore:
                    return;

                case DialoguePolicyOnNewTrigger.Replace:
                {
                    // Select first: the active dialogue is only aborted when a replacement exists.
                    DialogueGraph replacement = catalog.SelectGraph(triggerEvent.Npc, triggerEvent.EventType, triggerEvent.NpcState);
                    if(replacement == null)
                    {
                        if(verboseLogging) Debug.Log($"[DialogueSystem] Trigger '{triggerEvent.Npc}' ({triggerEvent.EventType}/{triggerEvent.NpcState}) matched no eligible graph; current dialogue kept.");
                        return;
                    }

                    runner.Abort();
                    StartGraph(replacement, triggerEvent);
                    return;
                }

                case DialoguePolicyOnNewTrigger.Queue:
                default:
                    waitingDialogues.Enqueue(triggerEvent);
                    return;
            }
        }

        private void TryStartQueued()
        {
            while(!runner.IsActive && waitingDialogues.Count > 0)
            {
                DialogueEvent triggerEvent = waitingDialogues.Dequeue();
                if(catalog != null && catalog.HasRule(triggerEvent.Npc, triggerEvent.EventType, triggerEvent.NpcState)) StartTrigger(triggerEvent);
            }
        }

        private void StartTrigger(DialogueEvent triggerEvent)
        {
            DialogueGraph graph = catalog != null ? catalog.SelectGraph(triggerEvent.Npc, triggerEvent.EventType, triggerEvent.NpcState) : null;

            if(graph == null)
            {
                if(verboseLogging) Debug.Log($"[DialogueSystem] Trigger '{triggerEvent.Npc}' ({triggerEvent.EventType}/{triggerEvent.NpcState}) matched no eligible graph.");
                return;
            }

            StartGraph(graph, triggerEvent);
        }

        private void StartGraph(DialogueGraph graph, DialogueEvent triggerEvent)
        {
            if(verboseLogging) Debug.Log($"[DialogueSystem] Trigger '{triggerEvent.Npc}' ({triggerEvent.EventType}/{triggerEvent.NpcState}) started dialogue '{(graph != null ? graph.name : "(null)")}'.");

            runner.Start(graph, triggerEvent, defaultLineDelaySeconds);
        }

        private void HandleLineDelivered(DialogueNodeData node, DialogueLine line)
        {
            DialogueLineEventArgs args = new DialogueLineEventArgs
            {
                Text = line != null && line.text != null ? line.text : string.Empty,
                TriggerNpc = runner.TriggerNpc,
                EventType = runner.EventType,
                NpcState = runner.NpcState,
                LineNumber = Mathf.Max(0, runner.LinesDelivered - 1)
            };

            Dialogue.RaiseLine(args);
        }

        private void HandleFinished()
        {
            Dialogue.RaiseEnded(new DialogueEndedEventArgs
            {
                TriggerNpc = runner.TriggerNpc,
                EventType = runner.EventType,
                NpcState = runner.NpcState,
                Completed = true
            });
        }

        private void HandleAborted()
        {
            Dialogue.RaiseEnded(new DialogueEndedEventArgs
            {
                TriggerNpc = runner.TriggerNpc,
                EventType = runner.EventType,
                NpcState = runner.NpcState,
                Completed = false
            });
        }
    }
}
