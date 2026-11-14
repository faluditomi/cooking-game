using System;
using UnityEngine;

namespace DialogueSystem
{
    /// <summary>
    /// The game-facing contract. Trigger dialogues in, subscribe to lines out.
    /// Requires a <see cref="DialogueDirector"/> in the active scene(s).
    /// </summary>
    public static class Dialogue
    {
        /// <summary>Fires for every delivered line.</summary>
        public static event Action<DialogueLineEventArgs> LineReceived;

        /// <summary>
        /// Fires once when a dialogue ends, after the last delivered line or when it is aborted.
        /// This is the recommended way for listeners to know a dialogue ended.
        /// </summary>
        public static event Action<DialogueEndedEventArgs> DialogueEnded;

        private static DialogueDirector activeDirector;

        public static bool IsDialogueActive
        {
            get { return activeDirector != null && activeDirector.IsActive; }
        }

        /// <summary>The NPC whose dialogue is currently playing, or <see cref="DialogueNpcId.None"/> when idle.</summary>
        public static DialogueNpcId ActiveTrigger
        {
            get { return activeDirector != null ? activeDirector.ActiveTrigger : DialogueNpcId.None; }
        }

        /// <summary>
        /// Raises a trigger for an NPC with an optional event type and NPC state. Handled on the
        /// director's next Update.
        /// </summary>
        public static void Trigger(DialogueNpcId npc, DialogueEventType eventType = DialogueEventType.None, DialogueNpcState npcState = DialogueNpcState.None)
        {
            if(activeDirector == null)
            {
                Debug.LogWarning($"[DialogueSystem] No DialogueDirector is active; trigger for '{npc}' ignored.");
                return;
            }

            if(npc == DialogueNpcId.None)
            {
                Debug.LogWarning("[DialogueSystem] Trigger ignored without an NPC.");
                return;
            }

            activeDirector.EnqueueTrigger(new DialogueEvent(npc, eventType, npcState));
        }

        internal static void Bind(DialogueDirector director)
        {
            activeDirector = director;
        }

        internal static void Unbind(DialogueDirector director)
        {
            if(activeDirector == director) activeDirector = null;
        }

        internal static void RaiseLine(DialogueLineEventArgs args)
        {
            LineReceived?.Invoke(args);
        }

        internal static void RaiseEnded(DialogueEndedEventArgs args)
        {
            DialogueEnded?.Invoke(args);
        }
    }
}
