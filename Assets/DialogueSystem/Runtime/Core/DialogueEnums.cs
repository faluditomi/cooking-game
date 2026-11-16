namespace DialogueSystem
{
    // Values 1 and 2 are deliberately skipped; they were Choice/Wait in earlier package versions and must not be reused.
    public enum DialogueNodeKind { Line = 0, Entry = 3, End = 4 }

    // FinishThenQueue: the current dialogue finishes; queued ones play after a delay, except a
    // priority trigger, which plays right after the current one and drops the rest of the queue.
    public enum DialoguePolicyOnNewTrigger { Queue = 0, Ignore = 1, Replace = 2, FinishThenQueue = 3 }
}
