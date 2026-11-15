using DialogueSystem;

namespace Cooking.Prototype
{
    /// <summary>
    /// Maps cooking moments onto DialogueSystem triggers: which event a served dish raises, which
    /// NPC state a stage is, and which (event, state) pair the catalog can actually answer.
    /// </summary>
    public static class CustomerDialogue
    {
        public static DialogueNpcState StateFor(NpcStage stage)
        {
            switch(stage)
            {
                case NpcStage.WarmedUp: return DialogueNpcState.WarmedUp;
                case NpcStage.Eureka: return DialogueNpcState.Eureka;
                default: return DialogueNpcState.Dejected;
            }
        }

        /// <summary>The event a served dish raises. Refusal and dream meal beat lateness.</summary>
        public static DialogueEventType EventFor(JudgeResult result)
        {
            if(result.Verdict == Verdict.Refused) return DialogueEventType.Refused;
            if(result.Verdict == Verdict.DreamMeal) return DialogueEventType.DreamMeal;
            if(result.Late && result.IsPaid) return DialogueEventType.LateDish;
            return result.IsHappy ? DialogueEventType.GoodDish : DialogueEventType.BadDish;
        }

        /// <summary>
        /// The state to trigger with: the stage itself when the catalog has lines for it, otherwise
        /// None (lines for any stage). Returns false when the catalog has nothing for the event at all.
        /// </summary>
        public static bool TryResolve(DialogueCatalog catalog, DialogueNpcId npc, DialogueEventType eventType, NpcStage stage,
            out DialogueNpcState state)
        {
            state = StateFor(stage);
            if(catalog == null || npc == DialogueNpcId.None) return false;
            if(catalog.HasRule(npc, eventType, state)) return true;

            state = DialogueNpcState.None;
            return catalog.HasRule(npc, eventType, state);
        }
    }
}
