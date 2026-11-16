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
        /// <summary>The customer arrives and orders. The graph's payload (or a [liked meal] / [dream dish] token) names the dish.</summary>
        Order = 1,
        /// <summary>Served exactly what they ordered.</summary>
        CorrectOrder = 2,
        /// <summary>Served something wrong that isn't good (too different, or something they won't eat).</summary>
        WrongOrderNasty = 3,
        // 4 and 5 were Refused and LateDish; keep them unused.
        /// <summary>The dish hit their secret desire: the dream order scene. Once per game.</summary>
        DreamOrder = 6,
        /// <summary>They've been waiting a while (restarts after each hurry).</summary>
        Hurry = 7,
        /// <summary>Served something else, but it's good (very close or close enough).</summary>
        WrongOrderTasty = 8,
        /// <summary>Mid-cooking chat, once per order after a random delay.</summary>
        Musing = 9,
    }

    // Game developer: extend this enum with the real NPC states your game uses. The package
    // cannot know game-specific values, so only None exists here. Add one member per state
    // (for example Idle, Busy, Angry) and register dialogue graphs against those members in
    // the DialogueCatalog. None is reserved for events that carry no NPC state and only
    // matches authored entries using None.
    //
    // Cooking game: how far the NPC has opened up (Miro board: Stranger / Familiar / Friendly).
    // Entries authored with None act as the fallback for any stage.
    public enum DialogueNpcState
    {
        None = 0,
        Stranger = 1,
        Familiar = 2,
        Friendly = 3,
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
