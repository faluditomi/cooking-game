namespace DialogueSystem
{
    /// <summary>Payload of <see cref="Dialogue.LineReceived"/>.</summary>
    public class DialogueLineEventArgs : System.EventArgs
    {
        /// <summary>Text of the delivered line.</summary>
        public string Text;

        /// <summary>The NPC whose dialogue is currently playing.</summary>
        public DialogueNpcId TriggerNpc;

        /// <summary>Event type from the raising trigger event.</summary>
        public DialogueEventType EventType;

        /// <summary>NPC state from the raising trigger event.</summary>
        public DialogueNpcState NpcState;

        /// <summary>0-based index of the delivered line within this dialogue.</summary>
        public int LineNumber;

        /// <summary>The graph being played (for its <see cref="DialogueGraph.payload"/>, name or one-shot flag).</summary>
        public DialogueGraph Graph;
    }

    /// <summary>
    /// Payload of <see cref="Dialogue.DialogueEnded"/>. Fires once after the last line of the
    /// dialogue has been delivered (Completed) or the dialogue was aborted (Completed = false).
    /// This is the recommended way for listeners to know a dialogue ended; subscribe via
    /// Dialogue.DialogueEnded.
    /// </summary>
    public class DialogueEndedEventArgs : System.EventArgs
    {
        /// <summary>The NPC whose dialogue ended.</summary>
        public DialogueNpcId TriggerNpc;

        /// <summary>Event type from the raising trigger event.</summary>
        public DialogueEventType EventType;

        /// <summary>NPC state from the raising trigger event.</summary>
        public DialogueNpcState NpcState;

        /// <summary>False when the dialogue was aborted (for example by a Replace policy).</summary>
        public bool Completed;

        /// <summary>The graph that was playing.</summary>
        public DialogueGraph Graph;
    }
}
