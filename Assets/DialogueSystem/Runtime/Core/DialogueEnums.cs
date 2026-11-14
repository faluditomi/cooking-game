namespace DialogueSystem
{
    // Values 1 and 2 are deliberately skipped; they were Choice/Wait in earlier package versions and must not be reused.
    public enum DialogueNodeKind { Line = 0, Entry = 3, End = 4 }

    public enum DialoguePolicyOnNewTrigger { Queue = 0, Ignore = 1, Replace = 2 }
}
