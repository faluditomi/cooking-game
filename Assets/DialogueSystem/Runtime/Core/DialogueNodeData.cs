using UnityEngine;

namespace DialogueSystem
{
    [System.Serializable]
    public class DialogueNodeData
    {
        public string guid;
        public DialogueNodeKind kind;

        public DialogueLine line = new DialogueLine(); // Line nodes

        // editor-only layout data; harmless at runtime
        public Vector2 editorPosition;
    }

    [System.Serializable]
    public class DialogueEdgeData
    {
        public string fromNodeGuid;

        // Legacy field: option edges no longer exist, so authored edges always leave this
        // empty. Runtime edge walking still ignores non-empty values as a safety net.
        public string fromOptionGuid;
        public string toNodeGuid;
    }
}
