using UnityEditor;
using UnityEngine;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// Stub inspector for <see cref="DialogueGraph"/>. Graphs are authored in the graph
    /// editor, which follows the current Project-window selection; the graph is already
    /// selected when this inspector is visible, so opening the window is enough.
    /// </summary>
    [CustomEditor(typeof(DialogueGraph))]
    public class DialogueGraphStubInspector : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit this graph in the Dialogue Graph Editor. " +
                "Custom Tools > Dialogue Graph Editor.",
                MessageType.Info);

            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isOneShot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("payload"));
            serializedObject.ApplyModifiedProperties();

            if(GUILayout.Button("Open Graph Editor"))
            {
                EditorApplication.ExecuteMenuItem("Custom Tools/Dialogue Graph Editor");
            }
        }
    }
}
