using UnityEditor;
using UnityEngine;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// Stub inspector for <see cref="DialogueCatalog"/>. Authoring happens in the dedicated
    /// <see cref="DialogueCatalogWindow"/> so the Inspector stays free of the group UI.
    /// </summary>
    [CustomEditor(typeof(DialogueCatalog))]
    public class DialogueCatalogEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Edit this catalog in the Dialogue Catalog Editor window. " +
                "Custom Tools > Dialogue Catalog Editor.",
                MessageType.Info);

            if(GUILayout.Button("Open Catalog Editor"))
            {
                EditorApplication.ExecuteMenuItem(DialogueCatalogWindow.MenuPath);
                DialogueCatalogWindow.ShowCatalog(target as DialogueCatalog);
            }
        }
    }
}
