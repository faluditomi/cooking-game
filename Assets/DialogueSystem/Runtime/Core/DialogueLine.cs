namespace DialogueSystem
{
    [System.Serializable]
    public class DialogueLine
    {
        [UnityEngine.TextArea(2, 5)]
        public string text;

        [UnityEngine.Tooltip("Seconds to wait before this line is delivered. Negative values use the director's default line delay.")]
        public float delaySeconds = 1f;
    }
}
