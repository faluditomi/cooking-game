namespace DialogueSystem
{
    // Game developer: extend this enum with the real event types your game raises. The
    // DialogueSystem package cannot know game-specific values, so only None exists here.
    // Add one member per event (for example Greet, Trade, QuestComplete) and register dialogue
    // graphs against those members in the DialogueCatalog. None is reserved for events that
    // carry no event type and only matches authored entries using None.
    public enum DialogueEventType { None = 0 }

    // Game developer: extend this enum with the real NPC states your game uses. The package
    // cannot know game-specific values, so only None exists here. Add one member per state
    // (for example Idle, Busy, Angry) and register dialogue graphs against those members in
    // the DialogueCatalog. None is reserved for events that carry no NPC state and only
    // matches authored entries using None.
    public enum DialogueNpcState { None = 0 }

    // Game developer: extend this enum with the real NPC identifiers your game uses. The package
    // cannot know game-specific values, so only None exists here. Add one member per NPC (for
    // example Mara, Merchant, Guard) and register dialogue graphs against those members in the
    // DialogueCatalog. None is reserved for triggers without an NPC; EnqueueTrigger rejects it.
    // This enum will be wired to actual NPC entities later.
    public enum DialogueNpcId { None = 0 }
}
