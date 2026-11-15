using System.Collections.Generic;
using DialogueSystem;
using DialogueSystem.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Builds placeholder dialogue for the regulars with the DialogueSystem's own editor API:
    /// one graph per (NPC, event, stage) and a catalog registering them, then wires the open
    /// prototype scene to it. Graphs that already exist are left alone, so rewriting the text in
    /// the Dialogue Graph Editor is safe; rerunning only adds what's missing.
    /// </summary>
    /// <remarks>
    /// Lines are placeholders written from the design doc's NPC notes. Dejected lines are terse,
    /// Warmed up lines drop clues towards each NPC's secret desire, Eureka lines pay it off.
    /// Entries with state None are the fallback for any stage.
    /// </remarks>
    public static class CookingDialogueSetup
    {
        private const string Folder = "Assets/Cooking/Data/Dialogue";
        private const string GraphFolder = Folder + "/Graphs";
        private const string CatalogPath = Folder + "/CookingDialogueCatalog.asset";

        private struct Spec
        {
            public DialogueNpcId Npc;
            public DialogueEventType Event;
            public DialogueNpcState State;
            public string[] Lines;

            public Spec(DialogueNpcId npc, DialogueEventType eventType, DialogueNpcState state, params string[] lines)
            {
                Npc = npc;
                Event = eventType;
                State = state;
                Lines = lines;
            }
        }

        private static List<Spec> Content()
        {
            const DialogueNpcState Any = DialogueNpcState.None;
            const DialogueNpcState Dejected = DialogueNpcState.Dejected;
            const DialogueNpcState Warm = DialogueNpcState.WarmedUp;
            const DialogueNpcState Eureka = DialogueNpcState.Eureka;

            DialogueNpcId woman = DialogueNpcId.Woman;
            DialogueNpcId man = DialogueNpcId.Man;
            DialogueNpcId sleeper = DialogueNpcId.Sleeper;
            DialogueNpcId eyesider = DialogueNpcId.Eyesider;

            return new List<Spec>
            {
                // The Woman: loud, unbothered by "the flux", avoids anything that reminds her of her love.
                // Desire: the sweet and sour soup she used to make for them.
                new Spec(woman, DialogueEventType.Order, Dejected, "Everyone's panicking about the flux. Ridiculous."),
                new Spec(woman, DialogueEventType.Order, Warm,
                    "You know what nobody on the Eye makes anymore? Anything with a bit of tang to it.",
                    "Someone I knew used to make a soup like that. Sweet. Sour. It doesn't matter."),
                new Spec(woman, DialogueEventType.Order, Eureka,
                    "I've been looking at shuttle schedules.",
                    "Don't look at me like that. Just cook."),
                new Spec(woman, DialogueEventType.GoodDish, Any, "Fine. That's fine."),
                new Spec(woman, DialogueEventType.GoodDish, Warm, "Not bad. Not bad at all.", "Greasy, salty. Nothing to think about. Good."),
                new Spec(woman, DialogueEventType.BadDish, Any, "Is this what passes for food now?"),
                new Spec(woman, DialogueEventType.Refused, Any, "I said no beef. Are you deaf?"),
                new Spec(woman, DialogueEventType.LateDish, Any, "Took you long enough."),
                new Spec(woman, DialogueEventType.Hurry, Any, "Any day now."),
                new Spec(woman, DialogueEventType.DreamMeal, Any,
                    "...",
                    "I used to make this. For someone.",
                    "I have to go. I have to find them before the flux finds me."),

                // The Man: works with orphans, careful about where food comes from.
                // Desire: something simple and tasty with nothing questionable in it (a hearty broth).
                new Spec(man, DialogueEventType.Order, Dejected, "The food on this station... Don't get me started."),
                new Spec(man, DialogueEventType.Order, Warm,
                    "I look after kids, down in the lower rings.",
                    "I just want something simple. Something I could make for them without feeling sick about where it came from."),
                new Spec(man, DialogueEventType.Order, Eureka, "I made it for the kids three times this week. They asked for it again."),
                new Spec(man, DialogueEventType.GoodDish, Any, "That's... decent. Thank you."),
                new Spec(man, DialogueEventType.BadDish, Any, "Healthy, I suppose. Joyless."),
                new Spec(man, DialogueEventType.Refused, Any, "Don't even get me started on the synthetic chicken."),
                new Spec(man, DialogueEventType.LateDish, Any, "The kids are waiting, you know."),
                new Spec(man, DialogueEventType.Hurry, Any, "I really need to get back."),
                new Spec(man, DialogueEventType.DreamMeal, Any,
                    "Simple. Tasty. Nobody got hurt making it.",
                    "Could I... could I make this for the kids?"),

                // The Sleeper: clueless, direct, can barely taste. Secretly a spice fiend.
                // Desire: something fiery that brings back their human parent.
                new Spec(sleeper, DialogueEventType.Order, Dejected, "Is that a thing people eat?"),
                new Spec(sleeper, DialogueEventType.Order, Warm,
                    "This place feels like home. I've never been here.",
                    "I remember heat. A kitchen that made your eyes water."),
                new Spec(sleeper, DialogueEventType.Order, Eureka, "I found her. She cooks like you do. Hotter, actually."),
                new Spec(sleeper, DialogueEventType.GoodDish, Any, "Wow! That kinda tastes.", "So that's why people stuff their face?"),
                new Spec(sleeper, DialogueEventType.BadDish, Any, "Uhh... thanks?"),
                new Spec(sleeper, DialogueEventType.LateDish, Any, "Was I supposed to be waiting?"),
                new Spec(sleeper, DialogueEventType.Hurry, Any, "Is it supposed to take this long? I don't know how food works."),
                new Spec(sleeper, DialogueEventType.DreamMeal, Any,
                    "That burn...",
                    "I remember a kitchen. Someone's hands.",
                    "I have to go. I know where she is."),

                // Generic Eyesiders: they give nothing.
                new Spec(eyesider, DialogueEventType.GoodDish, Any, "..."),
                new Spec(eyesider, DialogueEventType.BadDish, Any, "Tch."),
                new Spec(eyesider, DialogueEventType.LateDish, Any, "Tch. Slow."),
                new Spec(eyesider, DialogueEventType.DreamMeal, Any, "...huh."),
            };
        }

        [MenuItem("Custom Tools/Cooking Prototype/Build Dialogue")]
        public static void BuildDialogue()
        {
            DialogueCatalog catalog = EnsureCatalog();
            int createdGraphs = 0;

            foreach(Spec spec in Content())
            {
                DialogueGraph graph = EnsureGraph(spec, ref createdGraphs);
                Register(catalog, spec, graph);
            }

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            bool wired = WireOpenScene(catalog);
            Debug.Log("[Cooking] Dialogue: " + createdGraphs + " new graphs in " + GraphFolder + ", catalog at " + CatalogPath + "."
                      + (wired ? " The open prototype scene is wired to it." : " Open the prototype scene and run this again to wire it."));
        }

        private static DialogueCatalog EnsureCatalog()
        {
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Cooking/Data", "Dialogue");
            if(!AssetDatabase.IsValidFolder(GraphFolder)) AssetDatabase.CreateFolder(Folder, "Graphs");

            DialogueCatalog catalog = AssetDatabase.LoadAssetAtPath<DialogueCatalog>(CatalogPath);
            if(catalog != null) return catalog;

            catalog = ScriptableObject.CreateInstance<DialogueCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            return catalog;
        }

        private static DialogueGraph EnsureGraph(Spec spec, ref int created)
        {
            string stateName = spec.State == DialogueNpcState.None ? "Any" : spec.State.ToString();
            string path = GraphFolder + "/" + spec.Npc + " - " + spec.Event + " - " + stateName + ".asset";

            DialogueGraph graph = AssetDatabase.LoadAssetAtPath<DialogueGraph>(path);
            if(graph != null) return graph;

            graph = ScriptableObject.CreateInstance<DialogueGraph>();
            AssetDatabase.CreateAsset(graph, path);

            // Entry → Line → Line ... → End, laid out left to right like the graph editor's layout.
            DialogueNodeData previous = graph.AddNode(DialogueNodeKind.Entry);
            previous.editorPosition = new Vector2(0f, 0f);

            for(int i = 0; i < spec.Lines.Length; i++)
            {
                DialogueNodeData line = graph.AddNode(DialogueNodeKind.Line);
                line.line.text = spec.Lines[i];
                // First line comes quickly; later ones leave time to read.
                line.line.delaySeconds = i == 0 ? 0.4f : 1.8f;
                line.editorPosition = new Vector2(260f * (i + 1), 0f);
                graph.Connect(previous.guid, line.guid);
                previous = line;
            }

            DialogueNodeData end = graph.AddNode(DialogueNodeKind.End);
            end.editorPosition = new Vector2(260f * (spec.Lines.Length + 1), 0f);
            graph.Connect(previous.guid, end.guid);

            EditorUtility.SetDirty(graph);
            created++;
            return graph;
        }

        private static void Register(DialogueCatalog catalog, Spec spec, DialogueGraph graph)
        {
            DialogueNpcGroup group = catalog.groups.Find(g => g != null && g.npcId == spec.Npc);

            if(group == null)
            {
                group = new DialogueNpcGroup { npcId = spec.Npc };
                catalog.groups.Add(group);
            }

            bool registered = group.entries.Exists(e => e != null && e.graph == graph && e.eventType == spec.Event && e.npcState == spec.State);
            if(!registered) group.entries.Add(new DialogueEntry { graph = graph, eventType = spec.Event, npcState = spec.State });
        }

        /// <summary>
        /// Points the open scene's prototype at the catalog, gives its regulars their dialogue ids
        /// by name, and makes sure there is a DialogueDirector using the same catalog.
        /// </summary>
        private static bool WireOpenScene(DialogueCatalog catalog)
        {
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();
            if(prototype == null) return false;

            SerializedObject so = new SerializedObject(prototype);
            so.FindProperty("dialogueCatalog").objectReferenceValue = catalog;

            SerializedProperty customers = so.FindProperty("customers");

            for(int i = 0; i < customers.arraySize; i++)
            {
                SerializedProperty entry = customers.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("dialogueId").enumValueIndex = (int)IdForName(entry.FindPropertyRelative("name").stringValue);
            }

            so.ApplyModifiedProperties();

            DialogueDirector director = Object.FindAnyObjectByType<DialogueDirector>();
            if(director == null) director = new GameObject("Dialogue Director").AddComponent<DialogueDirector>();

            SerializedObject directorSo = new SerializedObject(director);
            directorSo.FindProperty("catalog").objectReferenceValue = catalog;
            // A new customer's lines cut off the last customer's instead of queueing behind them.
            directorSo.FindProperty("policyOnNewTrigger").enumValueIndex = (int)DialoguePolicyOnNewTrigger.Replace;
            directorSo.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
            return true;
        }

        /// <summary>Dialogue id for a regular from the prototype setup, by name.</summary>
        public static DialogueNpcId IdForName(string customerName)
        {
            switch(customerName)
            {
                case "The Woman": return DialogueNpcId.Woman;
                case "The Man": return DialogueNpcId.Man;
                case "The Sleeper": return DialogueNpcId.Sleeper;
                case "Eyesider": return DialogueNpcId.Eyesider;
                default: return DialogueNpcId.None;
            }
        }
    }
}
