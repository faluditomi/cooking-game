using UnityEngine;

namespace DialogueSystem
{
    /// <summary>
    /// Plain C# playback engine for ONE dialogue. It owns no Unity objects and applies no effects.
    /// It also never starts or resolves another dialogue synchronously; the director defers that
    /// to Update.
    /// </summary>
    public class DialogueRunner
    {
        /// <summary>Upper bound on node transitions processed in one Tick, to catch cycles.</summary>
        public const int MaxNodeStepsPerTick = 512;

        private enum PlayState { Idle, Playing, DelayedLine }

        private PlayState state = PlayState.Idle;
        private DialogueNodeData currentNode;

        // The single node line waiting for its delay to elapse.
        private DialogueNodeData scheduledOwner;
        private DialogueLine scheduledLine;
        private float lineTimer;

        public event System.Action<DialogueNodeData, DialogueLine> LineDelivered;
        public event System.Action Finished;
        public event System.Action Aborted;

        public bool IsActive => state != PlayState.Idle;

        public DialogueGraph Graph { get; private set; }
        public DialogueNpcId TriggerNpc { get; private set; }
        public DialogueEventType EventType { get; private set; }
        public DialogueNpcState NpcState { get; private set; }
        public int LinesDelivered { get; private set; }
        public float DefaultLineDelaySeconds { get; set; }

        public void Start(DialogueGraph graph, DialogueEvent triggerEvent, float defaultLineDelaySeconds)
        {
            ResetState();

            Graph = graph;
            TriggerNpc = triggerEvent.Npc;
            EventType = triggerEvent.EventType;
            NpcState = triggerEvent.NpcState;
            DefaultLineDelaySeconds = defaultLineDelaySeconds;

            if(graph == null)
            {
                Debug.LogWarning("[DialogueSystem] Start called with a null graph; ignored.");
                return;
            }

            DialogueNodeData entry = graph.GetEntryNode();

            if(entry == null)
            {
                Debug.LogWarning($"[DialogueSystem] Graph '{(graph != null ? graph.name : "(null)")}' has no nodes; ignored.");
                return;
            }

            currentNode = entry;
            int steps = 0;
            ProcessCurrentNode(ref steps);
        }

        /// <summary>
        /// Advances the scheduled line when its delay has elapsed. Zero-delay lines chain within
        /// this call, bounded by <see cref="MaxNodeStepsPerTick"/>.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if(state != PlayState.DelayedLine) return;

            lineTimer -= deltaTime;
            int steps = 0;

            while(state == PlayState.DelayedLine && lineTimer <= 0f)
            {
                if(++steps > MaxNodeStepsPerTick)
                {
                    WarnLikelyCycle();
                    Finish();
                    return;
                }

                DeliverScheduledLine();
                if(state != PlayState.Playing) return;

                ProcessCurrentNode(ref steps);
            }
        }

        /// <summary>Stops the dialogue without finishing its graph. Fires <see cref="Aborted"/>.</summary>
        public void Abort()
        {
            if(state == PlayState.Idle) return;

            state = PlayState.Idle;
            currentNode = null;
            ClearScheduledLine();
            Aborted?.Invoke();
        }

        private void ProcessCurrentNode(ref int steps)
        {
            state = PlayState.Playing;

            while(true)
            {
                if(++steps > MaxNodeStepsPerTick)
                {
                    WarnLikelyCycle();
                    Finish();
                    return;
                }

                DialogueNodeData node = currentNode;

                if(node == null)
                {
                    Finish();
                    return;
                }

                switch(node.kind)
                {
                    case DialogueNodeKind.Entry:
                        // Entry is a pure start marker.
                        Advance();
                        break;

                    case DialogueNodeKind.Line:
                        ScheduleNodeLine(node);
                        return;

                    case DialogueNodeKind.End:
                        Finish();
                        return;

                    default:
                        Finish();
                        return;
                }
            }
        }

        private void ScheduleNodeLine(DialogueNodeData owner)
        {
            scheduledOwner = owner;
            scheduledLine = owner.line;
            lineTimer = ResolveDelay(scheduledLine);
            state = PlayState.DelayedLine;
        }

        private float ResolveDelay(DialogueLine line)
        {
            float resolved = line != null && line.delaySeconds >= 0f ? line.delaySeconds : DefaultLineDelaySeconds;
            return Mathf.Max(0f, resolved);
        }

        private void DeliverScheduledLine()
        {
            DialogueNodeData owner = scheduledOwner;
            DialogueLine line = scheduledLine;

            ClearScheduledLine();
            state = PlayState.Playing;

            if(line != null)
            {
                // Increment BEFORE the event so the director's LineNumber = LinesDelivered - 1
                // is the 0-based index of this line.
                LinesDelivered++;
                LineDelivered?.Invoke(owner, line);
            }

            // Line nodes only deliver their line; graph edges alone decide what plays next.
            currentNode = owner;
            Advance();
        }

        private void Advance()
        {
            if(Graph == null || currentNode == null)
            {
                currentNode = null;
                return;
            }

            currentNode = Graph.GetNextNode(currentNode.guid);
        }

        // Graph/TriggerNpc stay populated so Finished listeners can report which dialogue ended.
        private void Finish()
        {
            state = PlayState.Idle;
            currentNode = null;
            ClearScheduledLine();
            Finished?.Invoke();
        }

        private void ClearScheduledLine()
        {
            scheduledOwner = null;
            scheduledLine = null;
            lineTimer = 0f;
        }

        private void ResetState()
        {
            state = PlayState.Idle;
            currentNode = null;
            ClearScheduledLine();
            Graph = null;
            TriggerNpc = DialogueNpcId.None;
            EventType = DialogueEventType.None;
            NpcState = DialogueNpcState.None;
            LinesDelivered = 0;
        }

        private void WarnLikelyCycle()
        {
            Debug.LogWarning($"[DialogueSystem] Dialogue '{(Graph != null ? Graph.name : "(null)")}' exceeded the per-tick node step budget; treating it as a loop and ending it.");
        }
    }
}
