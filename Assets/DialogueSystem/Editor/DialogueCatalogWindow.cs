using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// Standalone authoring window for <see cref="DialogueCatalog"/> assets.
    ///
    /// The Project window owns asset creation; this window only edits an existing catalog.
    /// It adopts whatever <see cref="DialogueCatalog"/> is selected in the Project window
    /// (unless the user picks one with the target field), and renders one collapsible
    /// section per NPC group with inline validation. All mutations flow through a
    /// <see cref="SerializedObject"/>/<see cref="SerializedProperty"/> pair, so Undo/Redo
    /// and asset persistence are handled by Unity - nothing is cached between GUI passes
    /// except the SerializedObject itself and per-index foldout state.
    /// </summary>
    public class DialogueCatalogWindow : EditorWindow
    {
        public const string MenuPath = "Custom Tools/Dialogue Catalog Editor";

        // Property paths must match DialogueCatalog / DialogueNpcGroup / DialogueEntry.
        // NOTE: the runtime group field is named 'npcId' (DialogueNpcGroup.npcId), not 'npc'.
        private const string GroupsPropertyName = "groups";
        private const string NpcPropertyName = "npcId";
        private const string EntriesPropertyName = "entries";
        private const string GraphPropertyName = "graph";
        private const string EventTypePropertyName = "eventType";
        private const string NpcStatePropertyName = "npcState";

        private DialogueCatalog targetCatalog;
        private SerializedObject catalogSerializedObject;
        private SerializedProperty groupsProperty;

        // Keyed by group index for the current target only; cleared whenever the target
        // changes so state from one catalog never leaks into another.
        private readonly List<bool> groupFoldouts = new List<bool>();

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            GetWindow<DialogueCatalogWindow>("Dialogue Catalog Editor");
        }

        /// <summary>
        /// Opens (or focuses) the window and makes it edit <paramref name="catalog"/>.
        /// Used by the stub inspector's button; the window also adopts Project selection
        /// on its own, but this makes the button deterministic even when the window is
        /// already open and pointed at another catalog.
        /// </summary>
        public static void ShowCatalog(DialogueCatalog catalog)
        {
            DialogueCatalogWindow window = GetWindow<DialogueCatalogWindow>("Dialogue Catalog Editor");
            window.SetTarget(catalog);
            window.Repaint();
        }

        private void OnEnable()
        {
            // OnEnable runs again after a domain reload without a guaranteed OnDisable;
            // unsubscribe first to keep the static event single-subscribed.
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;

            // Adopt whatever catalog was selected when the window opened from the menu.
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            Selection.selectionChanged -= OnSelectionChanged;
            targetCatalog = null;
            catalogSerializedObject = null;
            groupsProperty = null;
            groupFoldouts.Clear();
        }

        private void OnSelectionChanged()
        {
            // Follow Project-window selection whenever a catalog is selected. Selecting a
            // graph or other asset leaves the current target in place.
            DialogueCatalog selectedCatalog = Selection.activeObject as DialogueCatalog;

            if(selectedCatalog != null)
            {
                SetTarget(selectedCatalog);
            }

            Repaint();
        }

        public void SetTarget(DialogueCatalog catalog)
        {
            if(ReferenceEquals(targetCatalog, catalog)) return;

            targetCatalog = catalog;
            catalogSerializedObject = null;
            groupsProperty = null;
            groupFoldouts.Clear();
            Repaint();
        }

        private void OnGUI()
        {
            DrawTargetField();

            if(targetCatalog == null)
            {
                EditorGUILayout.HelpBox(
                    "Select a DialogueCatalog asset in the Project window or pick one above.",
                    MessageType.Info);
                return;
            }

            if(catalogSerializedObject == null
                || !ReferenceEquals(catalogSerializedObject.targetObject, targetCatalog))
            {
                catalogSerializedObject = new SerializedObject(targetCatalog);
                groupsProperty = null;
            }

            catalogSerializedObject.Update();

            // Re-find every pass: cheap for these asset sizes and immune to stale handles
            // after undo/redo or structural edits.
            groupsProperty = catalogSerializedObject.FindProperty(GroupsPropertyName);

            if(groupsProperty == null)
            {
                EditorGUILayout.HelpBox(
                    $"Could not find the serialized '{GroupsPropertyName}' field on {nameof(DialogueCatalog)}.",
                    MessageType.Error);
                return;
            }

            EnsureFoldoutListSize(groupsProperty.arraySize);

            DrawHeader();
            DrawValidationBox();
            EditorGUILayout.Space(4f);
            DrawGroups();

            catalogSerializedObject.ApplyModifiedProperties();
        }

        private void DrawTargetField()
        {
            EditorGUI.BeginChangeCheck();

            DialogueCatalog picked = (DialogueCatalog)EditorGUILayout.ObjectField(
                new GUIContent("Catalog", DialogueCatalogWindowTooltips.TargetField),
                targetCatalog,
                typeof(DialogueCatalog),
                false);

            if(EditorGUI.EndChangeCheck())
            {
                SetTarget(picked);
            }
        }

        private void EnsureFoldoutListSize(int groupCount)
        {
            while(groupFoldouts.Count < groupCount) groupFoldouts.Add(true);
            while(groupFoldouts.Count > groupCount) groupFoldouts.RemoveAt(groupFoldouts.Count - 1);
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField(
                new GUIContent("Dialogue Catalog", DialogueCatalogWindowTooltips.Header),
                EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                DialogueCatalogWindowTooltips.Header + "\n\n" + DialogueCatalogWindowTooltips.PlayOnceNote,
                MessageType.None);
        }

        #region Validation

        /// <summary>
        /// Rebuilds warnings on every repaint from the live SerializedProperty tree. The
        /// catalog is small, so this avoids any cache that could go stale after undo,
        /// redo, or an external asset change.
        /// </summary>
        private void DrawValidationBox()
        {
            List<string> warnings = CollectWarnings();

            if(warnings.Count > 0)
            {
                EditorGUILayout.HelpBox(string.Join("\n", warnings), MessageType.Warning);
                return;
            }

            if(groupsProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "No NPC groups yet. Use \"+ Add Group\" below to start authoring dialogue.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "No catalog issues detected. None/None event and state values are valid: " +
                "they match Trigger(npc) calls that do not supply an event type or state.",
                MessageType.Info);
        }

        private List<string> CollectWarnings()
        {
            List<string> warnings = new List<string>();
            Dictionary<int, int> firstGroupByNpc = new Dictionary<int, int>();

            for(int groupIndex = 0; groupIndex < groupsProperty.arraySize; groupIndex++)
            {
                SerializedProperty groupProperty = groupsProperty.GetArrayElementAtIndex(groupIndex);
                SerializedProperty npcProperty = groupProperty.FindPropertyRelative(NpcPropertyName);
                SerializedProperty entriesProperty = groupProperty.FindPropertyRelative(EntriesPropertyName);

                if(npcProperty == null || entriesProperty == null)
                {
                    warnings.Add($"Group {groupIndex + 1} is missing its serialized data.");
                    continue;
                }

                string npcLabel = GetEnumLabel(npcProperty);
                string groupLabel = $"Group {groupIndex + 1} ('{npcLabel}')";

                if(IsNone(npcProperty))
                {
                    // Dialogue.Trigger / DialogueDirector.EnqueueTrigger reject NPC None,
                    // so a group keyed by None can never be reached at runtime.
                    warnings.Add(
                        $"{groupLabel} uses NPC None and can never match; triggers require a non-None NPC.");
                }
                else
                {
                    // Compare the underlying enum value, not display names or indices.
                    int npcValue = npcProperty.intValue;

                    if(firstGroupByNpc.TryGetValue(npcValue, out int firstIndex))
                    {
                        warnings.Add(
                            $"Duplicate NPC '{npcLabel}' in groups {firstIndex + 1} and {groupIndex + 1}. " +
                            "Matching uses the first group.");
                    }
                    else
                    {
                        firstGroupByNpc[npcValue] = groupIndex;
                    }
                }

                if(entriesProperty.arraySize == 0)
                {
                    warnings.Add($"{groupLabel} has no entries.");
                    continue;
                }

                // Exact (eventType, npcState) sets behave as play-once bags keyed by graph
                // identity, so the same graph twice in one set is redundant. Different
                // sets may legitimately reuse a graph.
                Dictionary<string, HashSet<UnityEngine.Object>> graphsBySet =
                    new Dictionary<string, HashSet<UnityEngine.Object>>();

                for(int entryIndex = 0; entryIndex < entriesProperty.arraySize; entryIndex++)
                {
                    SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entryIndex);
                    SerializedProperty graphProperty = entryProperty.FindPropertyRelative(GraphPropertyName);
                    SerializedProperty eventTypeProperty = entryProperty.FindPropertyRelative(EventTypePropertyName);
                    SerializedProperty npcStateProperty = entryProperty.FindPropertyRelative(NpcStatePropertyName);

                    if(graphProperty == null || eventTypeProperty == null || npcStateProperty == null)
                    {
                        warnings.Add($"{groupLabel}, entry {entryIndex + 1}: entry data is missing or invalid.");
                        continue;
                    }

                    UnityEngine.Object graph = graphProperty.objectReferenceValue;

                    if(graph == null)
                    {
                        warnings.Add($"{groupLabel}, entry {entryIndex + 1}: no Dialogue Graph assigned.");
                        continue;
                    }

                    string setKey =
                        $"{eventTypeProperty.intValue}:{npcStateProperty.intValue}";

                    if(!graphsBySet.TryGetValue(setKey, out HashSet<UnityEngine.Object> graphsInSet))
                    {
                        graphsInSet = new HashSet<UnityEngine.Object>();
                        graphsBySet[setKey] = graphsInSet;
                    }

                    if(!graphsInSet.Add(graph))
                    {
                        string eventLabel = GetEnumLabel(eventTypeProperty);
                        string stateLabel = GetEnumLabel(npcStateProperty);

                        warnings.Add(
                            $"{groupLabel}, entry {entryIndex + 1}: graph '{graph.name}' is registered more than once " +
                            $"for Event Type '{eventLabel}' / NPC State '{stateLabel}'.");
                    }
                }
            }

            return warnings;
        }

        private static bool IsNone(SerializedProperty enumProperty)
        {
            string[] names = enumProperty.enumNames;
            int index = enumProperty.enumValueIndex;
            return index >= 0 && index < names.Length && names[index] == "None";
        }

        private static string GetEnumLabel(SerializedProperty enumProperty)
        {
            int index = enumProperty.enumValueIndex;

            if(index >= 0 && index < enumProperty.enumDisplayNames.Length)
            {
                return enumProperty.enumDisplayNames[index];
            }

            return index >= 0 ? index.ToString() : "?";
        }

        #endregion

        #region Groups

        private void DrawGroups()
        {
            int groupToRemove = -1;

            for(int groupIndex = 0; groupIndex < groupsProperty.arraySize; groupIndex++)
            {
                DrawGroup(groupIndex, ref groupToRemove);
            }

            if(groupToRemove >= 0)
            {
                groupsProperty.DeleteArrayElementAtIndex(groupToRemove);

                if(groupToRemove < groupFoldouts.Count)
                {
                    groupFoldouts.RemoveAt(groupToRemove);
                }
            }

            EditorGUILayout.Space(4f);

            if(GUILayout.Button(
                    new GUIContent("+ Add Group", DialogueCatalogWindowTooltips.AddGroup),
                    GUILayout.Height(24f)))
            {
                AddGroup();
            }
        }

        private void DrawGroup(int groupIndex, ref int groupToRemove)
        {
            SerializedProperty groupProperty = groupsProperty.GetArrayElementAtIndex(groupIndex);
            SerializedProperty npcProperty = groupProperty.FindPropertyRelative(NpcPropertyName);
            SerializedProperty entriesProperty = groupProperty.FindPropertyRelative(EntriesPropertyName);
            bool invalidGroup = npcProperty == null || entriesProperty == null;

            string summary = invalidGroup
                ? $"Group {groupIndex + 1} (invalid)"
                : BuildGroupSummary(npcProperty, entriesProperty.arraySize);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            if(invalidGroup)
            {
                EditorGUILayout.LabelField(summary, EditorStyles.boldLabel);
            }
            else
            {
                bool expanded = groupFoldouts[groupIndex];
                bool newExpanded = EditorGUILayout.Foldout(
                    expanded,
                    new GUIContent(summary, DialogueCatalogWindowTooltips.GroupHeader),
                    true,
                    EditorStyles.foldoutHeader);

                if(newExpanded != expanded)
                {
                    groupFoldouts[groupIndex] = newExpanded;
                }
            }

            if(GUILayout.Button(
                    new GUIContent("x", DialogueCatalogWindowTooltips.RemoveGroup),
                    EditorStyles.miniButton,
                    GUILayout.Width(22f),
                    GUILayout.Height(18f)))
            {
                groupToRemove = groupIndex;
            }

            EditorGUILayout.EndHorizontal();

            if(!invalidGroup && groupFoldouts[groupIndex])
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(
                    npcProperty,
                    new GUIContent("NPC", DialogueCatalogWindowTooltips.Npc));

                DrawEntries(entriesProperty);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2f);
        }

        private static string BuildGroupSummary(SerializedProperty npcProperty, int entryCount)
        {
            string entryLabel = entryCount == 1 ? "1 entry" : $"{entryCount} entries";
            return $"{GetEnumLabel(npcProperty)}  \u2014  {entryLabel}";
        }

        private void AddGroup()
        {
            int newIndex = groupsProperty.arraySize;
            groupsProperty.InsertArrayElementAtIndex(newIndex);

            SerializedProperty groupProperty = groupsProperty.GetArrayElementAtIndex(newIndex);
            SerializedProperty npcProperty = groupProperty.FindPropertyRelative(NpcPropertyName);
            SerializedProperty entriesProperty = groupProperty.FindPropertyRelative(EntriesPropertyName);

            // InsertArrayElementAtIndex duplicates the previous group; reset the fields so
            // the new group starts clean (NPC None, no entries).
            SetEnumToNone(npcProperty);
            if(entriesProperty != null) entriesProperty.arraySize = 0;

            if(groupFoldouts.Count < groupsProperty.arraySize)
            {
                groupFoldouts.Add(true);
            }
        }

        #endregion

        #region Entries

        private void DrawEntries(SerializedProperty entriesProperty)
        {
            EditorGUILayout.LabelField(
                new GUIContent("Entries", DialogueCatalogWindowTooltips.EntriesHeader),
                EditorStyles.boldLabel);

            if(entriesProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox(
                    "This group has no entries. Add at least one graph/event/state row to make it match.",
                    MessageType.Info);
            }

            int entryToRemove = -1;
            int entryToDuplicate = -1;

            for(int entryIndex = 0; entryIndex < entriesProperty.arraySize; entryIndex++)
            {
                SerializedProperty entryProperty = entriesProperty.GetArrayElementAtIndex(entryIndex);
                SerializedProperty graphProperty = entryProperty.FindPropertyRelative(GraphPropertyName);
                SerializedProperty eventTypeProperty = entryProperty.FindPropertyRelative(EventTypePropertyName);
                SerializedProperty npcStateProperty = entryProperty.FindPropertyRelative(NpcStatePropertyName);

                if(graphProperty == null || eventTypeProperty == null || npcStateProperty == null)
                {
                    EditorGUILayout.HelpBox(
                        $"Entry {entryIndex + 1} is missing serialized fields.",
                        MessageType.Error);
                    continue;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField(
                    new GUIContent($"Entry {entryIndex + 1}", DialogueCatalogWindowTooltips.EntryRow),
                    EditorStyles.boldLabel);

                GUILayout.FlexibleSpace();

                if(GUILayout.Button(
                        new GUIContent("Duplicate", DialogueCatalogWindowTooltips.DuplicateEntry),
                        EditorStyles.miniButton,
                        GUILayout.Width(70f)))
                {
                    entryToDuplicate = entryIndex;
                }

                if(GUILayout.Button(
                        new GUIContent("x", DialogueCatalogWindowTooltips.RemoveEntry),
                        EditorStyles.miniButton,
                        GUILayout.Width(22f)))
                {
                    entryToRemove = entryIndex;
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.PropertyField(
                    graphProperty,
                    new GUIContent("Graph", DialogueCatalogWindowTooltips.Graph));

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.PropertyField(
                    eventTypeProperty,
                    new GUIContent("Event Type", DialogueCatalogWindowTooltips.EventType));

                EditorGUILayout.PropertyField(
                    npcStateProperty,
                    new GUIContent("NPC State", DialogueCatalogWindowTooltips.NpcState));

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2f);
            }

            if(entryToDuplicate >= 0)
            {
                DuplicateEntry(entriesProperty, entryToDuplicate);
                return;
            }

            if(entryToRemove >= 0)
            {
                entriesProperty.DeleteArrayElementAtIndex(entryToRemove);
                return;
            }

            if(GUILayout.Button(
                    new GUIContent("+ Add Entry", DialogueCatalogWindowTooltips.AddEntry),
                    GUILayout.Height(22f)))
            {
                AddEntry(entriesProperty);
            }
        }

        private static void AddEntry(SerializedProperty entriesProperty)
        {
            int newIndex = entriesProperty.arraySize;
            entriesProperty.InsertArrayElementAtIndex(newIndex);
            ResetEntry(entriesProperty.GetArrayElementAtIndex(newIndex));
        }

        private static void DuplicateEntry(SerializedProperty entriesProperty, int sourceIndex)
        {
            SerializedProperty sourceProperty = entriesProperty.GetArrayElementAtIndex(sourceIndex);
            SerializedProperty sourceGraph = sourceProperty.FindPropertyRelative(GraphPropertyName);
            SerializedProperty sourceEventType = sourceProperty.FindPropertyRelative(EventTypePropertyName);
            SerializedProperty sourceNpcState = sourceProperty.FindPropertyRelative(NpcStatePropertyName);

            // Capture values before inserting, since insertion mutates the array. Use
            // intValue so the copy preserves the exact underlying enum value.
            UnityEngine.Object graph = sourceGraph != null ? sourceGraph.objectReferenceValue : null;
            int eventTypeValue = sourceEventType != null ? sourceEventType.intValue : 0;
            int npcStateValue = sourceNpcState != null ? sourceNpcState.intValue : 0;

            int newIndex = sourceIndex + 1;
            entriesProperty.InsertArrayElementAtIndex(newIndex);

            SerializedProperty duplicateProperty = entriesProperty.GetArrayElementAtIndex(newIndex);
            SerializedProperty duplicateGraph = duplicateProperty.FindPropertyRelative(GraphPropertyName);
            SerializedProperty duplicateEventType = duplicateProperty.FindPropertyRelative(EventTypePropertyName);
            SerializedProperty duplicateNpcState = duplicateProperty.FindPropertyRelative(NpcStatePropertyName);

            if(duplicateGraph != null) duplicateGraph.objectReferenceValue = graph;
            if(duplicateEventType != null) duplicateEventType.intValue = eventTypeValue;
            if(duplicateNpcState != null) duplicateNpcState.intValue = npcStateValue;
        }

        /// <summary>
        /// InsertArrayElementAtIndex can duplicate the previous row, so a freshly added
        /// entry is explicitly reset to the valid default: no graph, None event/state.
        /// </summary>
        private static void ResetEntry(SerializedProperty entryProperty)
        {
            SerializedProperty graphProperty = entryProperty.FindPropertyRelative(GraphPropertyName);
            SerializedProperty eventTypeProperty = entryProperty.FindPropertyRelative(EventTypePropertyName);
            SerializedProperty npcStateProperty = entryProperty.FindPropertyRelative(NpcStatePropertyName);

            if(graphProperty != null) graphProperty.objectReferenceValue = null;
            SetEnumToNone(eventTypeProperty);
            SetEnumToNone(npcStateProperty);
        }

        /// <summary>
        /// Selects the enum member named "None". Looking it up by name keeps this correct
        /// if the game later inserts members before or after None.
        /// </summary>
        private static void SetEnumToNone(SerializedProperty enumProperty)
        {
            if(enumProperty == null) return;

            string[] names = enumProperty.enumNames;

            for(int i = 0; i < names.Length; i++)
            {
                if(names[i] != "None") continue;
                enumProperty.enumValueIndex = i;
                return;
            }

            enumProperty.enumValueIndex = 0;
        }

        #endregion
    }

    /// <summary>
    /// Single reviewable table of window hover-tooltip wording. Each entry describes the
    /// runtime effect of the field rather than restating its label.
    /// </summary>
    internal static class DialogueCatalogWindowTooltips
    {
        public const string TargetField =
            "Catalog asset being edited. The window also follows the Project window: selecting " +
            "a different DialogueCatalog adopts it automatically.";

        public const string Header =
            "Maps an NPC enum value plus exact event type and NPC state to dialogue graphs. " +
            "The first group with the matching NPC wins. Event type and state must match " +
            "exactly; None/None is valid and matches Trigger(npc) calls without an event or state. " +
            "A group using NPC None can never match because triggers reject NPC None.";

        public const string PlayOnceNote =
            "Each exact (NPC, event type, state) set is a play-once bag: every distinct graph " +
            "plays once before random picking resumes, and played state is session-only.";

        public const string GroupHeader =
            "One NPC group. Only the first group with a given NPC value is used at runtime.";

        public const string Npc =
            "NPC enum value passed to Trigger(npc). Matching uses enum equality, and the first " +
            "group with this value wins. None can never match because triggers reject it.";

        public const string EntriesHeader =
            "One row per graph registered for this NPC. Event Type and NPC State narrow when the " +
            "graph is eligible; None/None is valid and matches bare Trigger(npc) calls.";

        public const string EntryRow =
            "A graph plus the exact event type and NPC state it responds to.";

        public const string Graph =
            "Dialogue graph played when this NPC, event type, and NPC state all match.";

        public const string EventType =
            "Event the game raised. Must match exactly; None is valid and matches bare Trigger(npc) calls.";

        public const string NpcState =
            "NPC state at trigger time. Must match exactly; None is valid and matches triggers " +
            "that do not supply a state.";

        public const string AddEntry =
            "Adds an empty graph/event/state row to this NPC group.";

        public const string RemoveEntry =
            "Removes this entry. The change is undoable with Ctrl+Z.";

        public const string DuplicateEntry =
            "Copies this row (graph, event type, and NPC state) into a new row immediately below.";

        public const string AddGroup =
            "Adds a new empty NPC group. Give it an NPC value and at least one entry.";

        public const string RemoveGroup =
            "Removes this entire NPC group. The change is undoable with Ctrl+Z.";
    }
}
