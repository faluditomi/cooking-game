namespace DialogueSystem
{
    // Game developer: extend this enum with the real event types your game raises. The
    // DialogueSystem package cannot know game-specific values, so only None exists here.
    // Add one member per event (for example Greet, Trade, QuestComplete) and register dialogue
    // graphs against those members in the DialogueCatalog. None is reserved for events that
    // carry no event type and only matches authored entries using None.
    //
    // Cooking game: raised by the cooking prototype at each moment of a customer's visit.
    // Keep the numbers stable; catalog assets store them.
    public enum DialogueEventType
    {
        None = 0,
        /// <summary>The customer places their order.</summary>
        Order = 1,
        /// <summary>Served on time and they're happy (their order, or a dish they like).</summary>
        GoodDish = 2,
        /// <summary>Served and they didn't like it.</summary>
        BadDish = 3,
        /// <summary>The dish has something they won't eat.</summary>
        Refused = 4,
        /// <summary>Served after the order timer ran out (they still pay).</summary>
        LateDish = 5,
        /// <summary>The dish hit their secret desire: the eureka moment.</summary>
        DreamMeal = 6,
        /// <summary>The order timer is running low.</summary>
        Hurry = 7,
    }

    // Game developer: extend this enum with the real NPC states your game uses. The package
    // cannot know game-specific values, so only None exists here. Add one member per state
    // (for example Idle, Busy, Angry) and register dialogue graphs against those members in
    // the DialogueCatalog. None is reserved for events that carry no NPC state and only
    // matches authored entries using None.
    //
    // Cooking game: how far the NPC has opened up (design doc: Dejected / Warmed up / Eureka).
    // Entries authored with None act as the fallback for any stage.
    public enum DialogueNpcState
    {
        None = 0,
        Dejected = 1,
        WarmedUp = 2,
        Eureka = 3,
    }

    // Game developer: extend this enum with the real NPC identifiers your game uses. The package
    // cannot know game-specific values, so only None exists here. Add one member per NPC (for
    // example Mara, Merchant, Guard) and register dialogue graphs against those members in the
    // DialogueCatalog. None is reserved for triggers without an NPC; EnqueueTrigger rejects it.
    // This enum will be wired to actual NPC entities later.
    //
    // Cooking game: the regulars from the design doc's NPC notes, plus a generic Eyesider.
    public enum DialogueNpcId
    {
        None = 0,
        Woman = 1,
        Man = 2,
        Sleeper = 3,
        Eyesider = 4,
    }
}
