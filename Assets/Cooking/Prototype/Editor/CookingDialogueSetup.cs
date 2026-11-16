using System.Collections.Generic;
using DialogueSystem;
using DialogueSystem.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Builds the regulars' dialogue with the DialogueSystem's own editor API: one graph per option
    /// (O1, O2, ...) of each (NPC, event, stage), registered in the catalog in that order (the catalog
    /// plays them in order the first time, then at random), then wires the open prototype scene to it.
    /// Graphs that already exist are left alone, so rewriting the text in the Dialogue Graph Editor is
    /// safe; rerunning only adds what's missing.
    /// </summary>
    /// <remarks>
    /// The Woman's lines are from the Cooking Game Miro board (Stranger / Familiar / Friendly, plus the
    /// dream order scene), as written. Order graphs carry a payload: a dish name, or a [liked meal] /
    /// [dream dish] token. [name of her lover] is left in the text until the board names her. The Man
    /// has the board's one MSG line; he and the others otherwise keep placeholders from the design doc.
    /// Entries with state None play at any stage.
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
            public int Option;
            public string Payload;
            public bool OneShot;
            public string[] Lines;
        }

        /// <summary>Collects specs, numbering options per (NPC, event, state) in the order they're added.</summary>
        private class Builder
        {
            public readonly List<Spec> Specs = new List<Spec>();
            private readonly Dictionary<string, int> options = new Dictionary<string, int>();

            public void Add(DialogueNpcId npc, DialogueEventType eventType, DialogueNpcState state, params string[] lines)
            {
                Add(npc, eventType, state, null, false, lines);
            }

            public void Add(DialogueNpcId npc, DialogueEventType eventType, DialogueNpcState state, string payload, bool oneShot, params string[] lines)
            {
                string key = npc + "|" + eventType + "|" + state;
                options.TryGetValue(key, out int option);
                options[key] = ++option;
                Specs.Add(new Spec { Npc = npc, Event = eventType, State = state, Option = option, Payload = payload, OneShot = oneShot, Lines = lines });
            }
        }

        private static List<Spec> Content()
        {
            const DialogueNpcState Any = DialogueNpcState.None;
            const DialogueNpcState Stranger = DialogueNpcState.Stranger;
            const DialogueNpcState Familiar = DialogueNpcState.Familiar;
            const DialogueNpcState Friendly = DialogueNpcState.Friendly;

            const DialogueEventType Order = DialogueEventType.Order;
            const DialogueEventType Musing = DialogueEventType.Musing;
            const DialogueEventType Hurry = DialogueEventType.Hurry;
            const DialogueEventType Correct = DialogueEventType.CorrectOrder;
            const DialogueEventType Tasty = DialogueEventType.WrongOrderTasty;
            const DialogueEventType Nasty = DialogueEventType.WrongOrderNasty;
            const DialogueEventType Dream = DialogueEventType.DreamOrder;

            const string MSBeef = "MSBeef fried rice";
            const string Liked = "[liked meal]";
            const string DreamDish = "[dream dish]";

            DialogueNpcId woman = DialogueNpcId.Woman;
            DialogueNpcId man = DialogueNpcId.Man;
            DialogueNpcId sleeper = DialogueNpcId.Sleeper;
            DialogueNpcId eyesider = DialogueNpcId.Eyesider;

            Builder b = new Builder();

            // ---------------------------------------------------------------- The Woman (Miro board)

            // Stranger
            b.Add(woman, Order, Stranger, MSBeef, false,
                "Whip me up an MSBeef rice real quick!",
                "And none of that beef shit, you hear me? I only eat Real Beef™!",
                "Fuck knows what they put in those animals...");
            b.Add(woman, Order, Stranger, Liked, false, "[liked meal]. Get on it!", "I'm starving.");
            b.Add(woman, Order, Stranger, Liked, false, "Need that [liked meal], Pot Boy.", "And try getting it done this cycle, ey?");
            b.Add(woman, Hurry, Stranger, "I really ain't got all day, Pot Boy.");
            b.Add(woman, Hurry, Stranger,
                "Did you hear they found one of the stall owners in peaces recently?",
                "Cut up in tiny little pieces... Behind that alley actually.",
                "Come to think of it, wasn't that your neighbour?");
            b.Add(woman, Hurry, Stranger, "aaaaaaaaAAAAAAHHHHH!!!", "What is even taking you this long!?");
            // O1 is "[Nothing]" on the board: she takes it and says nothing.
            b.Add(woman, Correct, Stranger);
            b.Add(woman, Correct, Stranger, "Be here tomorrow.", "Not like I got options.");
            b.Add(woman, Correct, Stranger, "Alright.");
            b.Add(woman, Tasty, Stranger, "You fucked up, Pot Boy.", "But I'll let you live...");
            b.Add(woman, Tasty, Stranger, "It'll do.", "But get it right next time.");
            b.Add(woman, Nasty, Stranger, "Can't read, can't cook.", "You're lucky you're the only food stall in the Bright Market.");
            b.Add(woman, Nasty, Stranger, "What even is this, Pot Boy?", "You call this cooking?");
            b.Add(woman, Nasty, Stranger, "Thanks, I guess I'll go hungry today...");

            // Familiar
            b.Add(woman, Order, Familiar, Liked, false, "Hey. Get me that [liked dish], will ya?", "Please.");
            b.Add(woman, Order, Familiar, MSBeef, false,
                "Craving an MSBeef fried rice, and no funky stuff.",
                "Don't take it the wrong way, Pot Boy. Not many chefs know their way around that beef animal.",
                "Not tryna start my cycle with a fucked up stomach.");
            b.Add(woman, Musing, Familiar,
                "Look at all these cowards roaming the market.",
                "The Flux comes knocking a few times and they all scramble to leave everything behind.",
                "Taking all their fancy beef with them, leaving only us real ones behind.",
                "Pathetic...",
                "For real, though. You can barely scrape together some beef these days.",
                "Not like I'd want to anyway...");
            b.Add(woman, Musing, Familiar,
                "Been here long, Pot Boy?",
                "Build a life here, I assume. Like anyone with a sliver of perseverance in their guts.",
                "Lost some people too, ey?",
                "Didn't we all?",
                "The Flux sours all sweetness before long.");
            b.Add(woman, Musing, Familiar,
                "You know, I've been meaning to tell you. Even the sour notes hit right in your cooking.",
                "That's hard to do, Pot Boy. You should be proud.");
            b.Add(woman, Hurry, Familiar,
                "Ya know, when I woke up this morning, I didn't expect I'd spend half my cycle watching you tryna whip up a passing dish.");
            b.Add(woman, Hurry, Familiar,
                "I haven't been here much, but even I know better how your kitchen-eque patchwork station functions.",
                "Need help finding something?");
            b.Add(woman, Hurry, Familiar, "I don't come here for fun...", "Are we clear on that?");
            b.Add(woman, Correct, Familiar, "That hits the spot.", "I'll see you around, ey?");
            b.Add(woman, Correct, Familiar, "Thanks.", "Reminds me of the stuff I used to make. 'Twas a long time ago now...", "How time flies, Pot Boy.");
            b.Add(woman, Tasty, Familiar, "Alright! Pot Boy got skills.", "Why you tryna hide these recipes from me, ey?");
            b.Add(woman, Tasty, Familiar, "Mmmm, damn!", "The fuck else are you hiding from me?");
            b.Add(woman, Tasty, Familiar, "This...", "This is good shit, Pot Boy.", "Update your menu, for Flux' sake!");
            b.Add(woman, Nasty, Familiar, "Is that you, Pot Boy? Am I at the wrong stall?", "Why would you do me like this?");
            b.Add(woman, Nasty, Familiar, "You and me will pretend this never happened.", "I'll giving you another chance.");

            // The dream order scene (between Familiar and Friendly): once per game.
            b.Add(woman, Dream, Any, null, true,
                "This... This is...",
                "It can't be.",
                "I know this soup, I used to make it over and over again for...",
                "Ohh, [name of her lover], where might you be be now. Why did you have to leave it all behind... Leave me behind...",
                "Pot Boy, who the fuck are you? Why do you know my recipe?",
                "The balance of sweet and sour, the thickness of the broth, and the beef!?",
                "The beef is tender, just the way she liked it. Just the way she always was.",
                "[name of her lover] is my partner. Or, was my partner. Once the Flux events started she wanted nothing more to do with this place and left.",
                "More like I let her go, the bonehead that I am.",
                "She was smart to leave and I was too caught up in the sour to see the sweetness slip away. Been caught up ever since.",
                "I have to go now.",
                "I have to go but I'll be back. I'm not done with you yet.");

            // Friendly
            b.Add(woman, Order, Friendly, DreamDish, false, "Hey Pot Boy.", "Get me that [dream dish], will ya?");
            b.Add(woman, Order, Friendly, Liked, false, "Hit me with that [liked dish], boss!");
            b.Add(woman, Order, Friendly, Liked, false, "Hey Boss!", "Will take a [liked dish]. Unless you're up for some... Magic.");
            b.Add(woman, Musing, Friendly, null, true,
                "I still don't know how you did that by the way.",
                "Dusty ass market cook turns out not just to be a beef boss, but a clairvoyant recipe thief, sending my brain reeling with some street food.",
                "Anyways... Your soup made me realise some things I should have realised a long time ago.",
                "All those cycles ago, I fucked up majorly.",
                "I shouldn't have let her leave on her own, and though I might have fucked it permanently, I have to try and make things right.",
                "I have to make [name of her lover] her favourite soup once more.",
                "What I'm trying to say is thank you, I owe you one.",
                "But don't expect no tips from me. Especially now that I have to buy my way off this piece of metal.");
            b.Add(woman, Musing, Friendly,
                "Catch this, I got a lead on [name of her lover].",
                "She hitched a ride on a cargo run that still comes around today. It's coming out of an agriculture sector not far from here.",
                "Dunno where she headed from there, but hey, it's a start.",
                "Maybe I'll even see how your precious potatoes are created.",
                "Or... Maybe it's a beef factory...",
                "Fuck, I didn't think of that!");
            b.Add(woman, Musing, Friendly,
                "Get this, Pot Boy!",
                "This blockhead just secured some tix out of here.",
                "That's right, few weeks time, imma be clean out. Looking for someone I last seen many many lonely birthdays ago. Tyna cook for them.",
                "Fuckin' terrifying...");
            b.Add(woman, Hurry, Friendly, "Umm... Yeah...", "Take your time, boss.");
            b.Add(woman, Hurry, Friendly, "You know, if the grub wasn't this good, I wouldn't put up with this, Pot Boy.");
            b.Add(woman, Hurry, Friendly,
                "I was already hungry when I got here.",
                "Keeping me in suspense doesn't make me more hungry.",
                "Only more fu--",
                "I mean, nothing. This is great.");
            b.Add(woman, Correct, Friendly, "Ohh yeah, That's the stuff.", "Have a good one Pot Boy, I'll see you 'round.");
            b.Add(woman, Correct, Friendly, "Mmm...", "Maybe I should take you with me, ey?", "Yeah, I'll miss this.");
            b.Add(woman, Tasty, Friendly,
                "Dunno how you does it, and to be honest, I don't care either.",
                "Just keep doing what you're doing.",
                "Same time tomorrow.");
            b.Add(woman, Tasty, Friendly, "Ohhh... The boss pulls out another one.", "Be back soon, alright?");
            b.Add(woman, Tasty, Friendly, "All this taste... Obliterating my mouth right now.", "I fuckin' love it.");
            b.Add(woman, Nasty, Friendly, "Is this your evil twin, or what?", "Did you sleep alright? I see some bad energies under your eyes.");
            b.Add(woman, Nasty, Friendly, "No, not evil Pot Boy again...", "Now that I look at you, you do look about as bad today as this meal.");
            b.Add(woman, Nasty, Friendly, "You know, I'm not disappointed, just fucking mad.");

            // ---------------------------------------------------------------- The Man (placeholders + the board's MSG line)

            b.Add(man, Order, Stranger, "The food on this station... Don't get me started.");
            b.Add(man, Order, Familiar,
                "I look after kids, down in the lower rings.",
                "I just want something simple. Something I could make for them without feeling sick about where it came from.");
            b.Add(man, Order, Friendly, "I made it for the kids three times this week. They asked for it again.");
            b.Add(man, Musing, Familiar, "Did you know MSG stands for Major Societal deGeneracy?", "And they made two of them!", "The bastards.");
            b.Add(man, Hurry, Any, "I really need to get back.");
            b.Add(man, Correct, Any, "That's... decent. Thank you.");
            b.Add(man, Tasty, Any, "It's... not quite what I asked for. It's fine.");
            b.Add(man, Nasty, Any, "Healthy, I suppose. Joyless.");
            b.Add(man, Nasty, Any, "Don't even get me started on the synthetic chicken.");
            b.Add(man, Dream, Any, null, true, "Simple. Tasty. Nobody got hurt making it.", "Could I... could I make this for the kids?");

            // ---------------------------------------------------------------- The Sleeper (placeholders; the board's template is empty)

            b.Add(sleeper, Order, Stranger, "Is that a thing people eat?");
            b.Add(sleeper, Order, Familiar,
                "This place feels like home. I've never been here.",
                "I remember heat. A kitchen that made your eyes water.");
            b.Add(sleeper, Order, Friendly, "I found her. She cooks like you do. Hotter, actually.");
            b.Add(sleeper, Hurry, Any, "Is it supposed to take this long? I don't know how food works.");
            b.Add(sleeper, Correct, Any, "Wow! That kinda tastes.", "So that's why people stuff their face?");
            b.Add(sleeper, Tasty, Any, "Is it supposed to be like this? I honestly can't tell.");
            b.Add(sleeper, Nasty, Any, "Uhh... thanks?");
            b.Add(sleeper, Dream, Any, null, true, "That burn...", "I remember a kitchen. Someone's hands.", "I have to go. I know where she is.");

            // ---------------------------------------------------------------- Generic Eyesiders: they give nothing.

            b.Add(eyesider, Correct, Any, "...");
            b.Add(eyesider, Tasty, Any, "Mm.");
            b.Add(eyesider, Nasty, Any, "Tch.");
            b.Add(eyesider, Dream, Any, null, true, "...huh.");

            return b.Specs;
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
            string path = GraphFolder + "/" + spec.Npc + " - " + spec.Event + " - " + stateName + " - O" + spec.Option + ".asset";

            DialogueGraph graph = AssetDatabase.LoadAssetAtPath<DialogueGraph>(path);
            if(graph != null) return graph;

            graph = ScriptableObject.CreateInstance<DialogueGraph>();
            graph.isOneShot = spec.OneShot;
            graph.payload = spec.Payload ?? "";
            AssetDatabase.CreateAsset(graph, path);

            // Entry → Line → Line ... → End, laid out left to right like the graph editor's layout.
            DialogueNodeData previous = graph.AddNode(DialogueNodeKind.Entry);
            previous.editorPosition = new Vector2(0f, 0f);

            for(int i = 0; i < spec.Lines.Length; i++)
            {
                DialogueNodeData line = graph.AddNode(DialogueNodeKind.Line);
                line.line.text = spec.Lines[i];
                // The first line comes quickly; each later one waits long enough to read the one before.
                line.line.delaySeconds = i == 0 ? 0.4f : Mathf.Round((1f + spec.Lines[i - 1].Length / 28f) * 10f) / 10f;
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
            // What's being said finishes; the next line waits a beat, and a reaction to the dish jumps the queue.
            directorSo.FindProperty("policyOnNewTrigger").enumValueIndex = (int)DialoguePolicyOnNewTrigger.FinishThenQueue;
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
