# Dialogue Kit - a general-purpose dialogue system

Dialogue Kit is a drop-in Unity package for event-driven dialogue. It is deliberately
game-agnostic: it selects a dialogue graph from a catalog, plays a linear sequence of timed
lines, and reports when the dialogue ends. Gameplay state, consequences, branching decisions
and presentation stay with your game.

The package was extracted and generalized from the conversation-graph system in the Dream
Deficit prototype. It keeps the graph authoring workflow (Entry / Line / End nodes and a
GraphView editor window) but removes all chat-window, typing indicator, day progression, flag
scoring, Addressables and save-system coupling.

- Unity: 6000.0 (Unity 6) or newer.
- Dependencies: none beyond the Unity engine. Runtime has no TextMeshPro, uGUI,
  Addressables or save-system dependencies.
- Ships as two assemblies (`DialogueSystem.Runtime`, `DialogueSystem.Editor`) and is safe to
  drop into `Assets/` or `Packages/` of any project.

---

## 1. The contract (read this first)

Your game only needs one input channel and two output events:

1. **In:** `Dialogue.Trigger(DialogueNpcId npc, DialogueEventType eventType = None,
   DialogueNpcState npcState = None)` - request a dialogue for an NPC, optionally narrowed by
   an event type and NPC state. The facade rejects `DialogueNpcId.None`.
2. **Out:** `Dialogue.LineReceived` - fires for every delivered line with the text, the
   triggering NPC/event/state, and the 0-based line number.
3. **Out:** `Dialogue.DialogueEnded` - **the recommended way for listeners to know a dialogue
   ended.** It fires once after the last line has been delivered (`Completed == true`) or when
   the dialogue is aborted (`Completed == false`). Do not infer completion from the last line;
   an abort is explicit rather than inferred.

That is the whole integration surface. The package decides:

- whether a trigger starts a dialogue (does the catalog have an exact NPC/event/state match?);
- which graph plays (uniform random among the matching set's not-yet-played graphs - the
  play-once bag);
- when each line appears (per-line delay);
- when the dialogue ends (End node, missing outgoing edge, or abort).

`DialogueEnded` is a first-class signal, not an optional convenience. Effects and world state
are handled by the game: subscribe to `DialogueEnded` and apply consequences there (set flags,
advance quests, spawn something, raise another trigger). The package itself stores no gameplay
state.

Optional read-only helpers exist (`Dialogue.IsDialogueActive`, `Dialogue.ActiveTrigger`), but a
game can ignore them and still work.

Constraint: **exactly one dialogue is active at a time.** The director owns a single
`DialogueRunner`.

---

## 2. Installation

The folder that contains this README is a complete Unity package:

```
DialogueSystem/
  package.json
  README.md
  Runtime/   (DialogueSystem.Runtime.asmdef)
    Core/DialogueEnums.cs             node/policy enums (numeric holes preserved)
    Core/DialogueEventEnums.cs        game-extended NPC / event type / NPC state enums
    Core/DialogueLine.cs              text + delaySeconds
    Core/DialogueNodeData.cs          DialogueNodeData, DialogueEdgeData
    Core/DialogueGraph.cs             ScriptableObject graph asset
    Core/DialogueCatalog.cs           ScriptableObject catalog asset
    DialogueEventData.cs              line / ended event payloads
    Dialogue.cs                       static facade
    DialogueRunner.cs                 plain C# playback state machine
    DialogueDirector.cs               MonoBehaviour: trigger pipeline, policy, events
  Editor/    (DialogueSystem.Editor.asmdef, Unity 6 GraphView authoring tools)
    DialogueCatalogWindow.cs          standalone catalog authoring window
    DialogueAssetInspectors.cs        stub graph Inspector
    DialogueCatalogEditor.cs          stub catalog Inspector
    DialogueGraphEditor.cs            graph editor window
    DialogueGraphView.cs              GraphView shell
    DialogueNodeView.cs               GraphView node view
    DialogueGraphEditorExtensions.cs  editor-only graph mutation helpers
```

Copy the whole folder into either location of the target project:

- `Packages/com.dialoguekit.dialogue-system/` - embedded package (recommended; shows up in the Package Manager; the directory name must match the package name in `package.json`).
- `Assets/DialogueSystem/` - plain assets. The `.asmdef` files keep the assemblies isolated
  either way. Unity ignores `package.json` inside `Assets/`.

Do not copy `.meta` files from another project; let Unity generate them on import. Do not
move files between `Runtime/` and `Editor/` - the assemblies are separated on purpose.

---

## 3. Quick start

1. Edit `Runtime/Core/DialogueEventEnums.cs` to add your NPC identifiers (`DialogueNpcId`) and,
   if you need them, event types (`DialogueEventType`) and NPC states (`DialogueNpcState`). Each
   enum starts with `None = 0`.
2. Create assets via **Assets > Create > Dialogue System > Dialogue Graph** (one asset per
   dialogue) and **Assets > Create > Dialogue System > Dialogue Catalog** (one asset that groups
   dialogue graphs per NPC). Selecting either asset shows a stub Inspector with an **Open
   Editor** button; authoring happens in the two dedicated windows.
3. Open **Custom Tools > Dialogue Graph Editor**, select the graph asset in the Project window,
   and author the graph (right-click the canvas to add Line / Entry / End nodes; select a node
   to edit its Message and Delay in the inspector pane). Run **Validate Graph** from the context
   menu when done.
4. Open **Custom Tools > Dialogue Catalog Editor** and register the graph for an NPC, event type
   and NPC state. The window follows the catalog selected in the Project window.
5. Add a `DialogueDirector` component to a scene GameObject and assign the catalog.
6. Subscribe and trigger from game code:

```csharp
using DialogueSystem;
using UnityEngine;

public class IntroFlow : MonoBehaviour
{
    private void OnEnable()
    {
        Dialogue.LineReceived += HandleLine;
        Dialogue.DialogueEnded += HandleEnded;
    }

    private void OnDisable()
    {
        Dialogue.LineReceived -= HandleLine;
        Dialogue.DialogueEnded -= HandleEnded;
    }

    private void Start()
    {
        // The director defers these to its next Update; lines stream out afterwards.
        // Bare form: event/state are None/None and match entries authored as None/None.
        Dialogue.Trigger(DialogueNpcId.Mara);

        // Explicit form: match a specific event type and NPC state authored in the catalog.
        Dialogue.Trigger(DialogueNpcId.Mara, DialogueEventType.Greet, DialogueNpcState.Idle);
    }

    private void HandleLine(DialogueLineEventArgs line)
    {
        // Build a chat bubble / subtitle / text log from line.Text.
        Debug.Log($"[{line.TriggerNpc}/{line.EventType}/{line.NpcState}] #{line.LineNumber}: {line.Text}");
    }

    private void HandleEnded(DialogueEndedEventArgs ended)
    {
        // The first-class completion signal: apply consequences here.
        Debug.Log($"Dialogue with {ended.TriggerNpc} ended (completed: {ended.Completed}).");
    }
}
```

Your UI is not part of the package. You decide how a line is displayed (chat window,
subtitles, dialogue box, log) and how fast your own UI reveals text. The package only tells
you which line is "now" and when the dialogue is over.

---

## 4. Core concepts

### 4.1 Triggers

A trigger is a non-empty NPC enum value, optionally paired with a `DialogueEventType` and a
`DialogueNpcState`:

```csharp
Dialogue.Trigger(DialogueNpcId.Mara);                                  // None / None
Dialogue.Trigger(DialogueNpcId.Mara, DialogueEventType.Greet, DialogueNpcState.Idle);
```

Triggers are:

- **matched against the catalog** by NPC enum equality plus the exact event-type/state pair,
  to pick a graph;
- **deferred**: `Trigger` only queues the request; the director drains it on its next `Update`.
  Nothing starts synchronously while a runner is mid-walk;
- **not latched**: there is no global dialogue state container and no stored trigger history. A
  trigger is an enum triple passed through the queue, and that is all. If a trigger arrives
  while a dialogue is active, the director applies its policy (see 4.3).

`DialogueNpcId.None` is rejected by the facade (with a warning) and again by
`DialogueDirector.EnqueueTrigger`. A trigger without a real NPC can never start a dialogue.

### 4.2 NPC groups and play-once bags

A `DialogueCatalog` asset is a list of `DialogueNpcGroup`s, one per NPC:

| Field     | Meaning                                                                                       |
|-----------|-----------------------------------------------------------------------------------------------|
| `npcId`   | `DialogueNpcId` passed to `Trigger`. Matching uses enum equality; the first group with that value wins. |
| `entries` | `List<DialogueEntry>` - the graphs registered for this NPC.                                    |

Each `DialogueEntry` is a graph plus the exact event type and NPC state it responds to:

| Field       | Meaning                                                                                  |
|-------------|------------------------------------------------------------------------------------------|
| `graph`     | The `DialogueGraph` to play.                                                             |
| `eventType` | `DialogueEventType` from the trigger. Must match exactly; defaults to `None`.            |
| `npcState`  | `DialogueNpcState` from the trigger. Must match exactly; defaults to `None`.             |

`DialogueEventType`, `DialogueNpcState` and `DialogueNpcId` only contain `None` in the package.
Extend them in `Runtime/Core/DialogueEventEnums.cs` with your game's real values - the package
cannot know them. `None`/`None` is valid for the event/state fields and matches bare
`Trigger(npc)` calls; `DialogueNpcId.None` is never valid for a trigger.

When a trigger starts a dialogue:

1. The first group whose `npcId` equals the trigger's NPC (enum equality) is used. Later
   duplicate groups are ignored at runtime (the catalog window warns about them).
2. Every entry whose `eventType` and `npcState` both equal the trigger's pair contributes its
   graph. Duplicate graph references within one exact set count once.
3. That exact set is a **play-once bag**: the first time through, graphs play **in authored
   order** (the order of the entries in the group), each once. The selected graph is marked
   played and starts from its Entry node. Different sets track their plays independently.
4. When every graph in the set has played, picking becomes uniform random over the set.
5. A graph with **`isOneShot`** plays at most once per session (until `ResetPlayed()`), across
   every set it belongs to. After it has played it is never picked again, even in the random
   phase. A set made only of spent one-shots plays nothing.

There are no conditions and no weights on candidates: an entry either matches the exact
(npc, eventType, npcState) set or it does not.

Each graph also has a free-text **`payload`** the package never reads. The game can use it to
tag a graph (for example, the dish an order dialogue names); it arrives with every line and
ended event through `Graph`.

If no group or entry matches, nothing plays (enable `verboseLogging` on the director to see
why). Played state is session-only `[System.NonSerialized]` bookkeeping on the catalog instance,
keyed by (npc, event type, NPC state); a dialogue never carries this history. It is never
written into the asset and does not survive domain reload or save/load.
`DialogueCatalog.ResetPlayed()` clears it. Call it on a new game or load when every graph should
become eligible again.

`DialogueCatalog.HasRule(...)` reports whether any authored entry matches and deliberately
ignores the play-once state, so the director's policy path still recognizes a set that has
already been played.

### 4.3 One dialogue at a time

While a dialogue is active, a new trigger that also has an exact catalog match is handled by
the director's `policyOnNewTrigger` setting:

| Policy    | Behaviour                                                                                     |
|-----------|-----------------------------------------------------------------------------------------------|
| `Queue`   | The trigger is queued. When the current dialogue ends, the oldest queued trigger starts and its catalog matching (NPC + event-type/state pair) is re-evaluated at start. |
| `Ignore`  | The trigger is dropped.                                                                        |
| `Replace` | Catalog matching is evaluated first. If a graph is eligible, the active dialogue is aborted (`DialogueEnded.Completed == false`) and the new one starts in the same frame. If nothing matches, the current dialogue continues. |
| `FinishThenQueue` | The current dialogue always finishes. Queued triggers start one at a time, each `queuedDialogueDelaySeconds` after the previous dialogue ended. A trigger raised with `priority: true` clears the queue and starts right after the current dialogue (no delay); while it waits, ordinary triggers are dropped. |

`Dialogue.Trigger(npc, eventType, npcState, priority)` sets the priority flag; the other
policies ignore it.

`Dialogue.StopAll()` aborts the playing dialogue (`DialogueEnded.Completed == false`) and drops
every queued trigger, for when the scene moves on (a customer leaving, a day ending).

While idle, queued dialogues start before a fresh trigger is considered. If a fresh trigger
arrives in the same drain after a queued dialogue starts, it is handled by the policy above.

At most 256 triggers are drained per frame. A larger flood drops the remaining triggers with a
warning instead of hanging the frame; this usually means a dialogue keeps re-raising the trigger
that started it.

### 4.4 Timing model

- A dialogue starts on the director's next `Update` after `Dialogue.Trigger(...)`.
- Each Line node has `delaySeconds`: seconds to wait **before that line is delivered**, relative
  to the previous delivered line (or the trigger, for the first line). A negative value uses
  the director's `defaultLineDelaySeconds`; the resolved delay is clamped to zero.
- There is no typing indicator and no separate "delay then type" phase. One delay per line.
- After a line is delivered, playback advances along that Line node's single outgoing edge.
  Reaching an End node ends the dialogue. A Line with no outgoing edge also ends the dialogue
  (an End node is optional but recommended).
- Zero-delay lines chain within one runner tick, bounded by `MaxNodeStepsPerTick = 512`.
  Exceeding it logs a warning, treats the graph as a loop, and ends the dialogue. A dialogue
  starts during trigger draining, after that frame's runner tick, so even a zero-delay **first**
  line is delivered on the next `Update`.
- A dialogue that reaches End (or a missing edge) with no lines still raises
  `DialogueEnded`; `LineReceived` simply never fires for it.

Per-frame order inside the director:

1. tick the runner (deliver a due line and advance the graph);
2. drain pending triggers (start / queue / replace dialogues).

### 4.5 Graph playback model

A graph is effectively a chain, not a general branching network:

- Playback starts at the first Entry node (in node list order). Entry is a pure start marker:
  it has no line and no effects.
- Every Line delivers its line, then follows its single outgoing edge.
- Reaching an End node ends the dialogue. A Line with no outgoing edge also ends the dialogue.
- The editor enforces single-fanout outputs, so only one route exists per graph. If legacy
  asset data contains more than one outgoing edge from a node, runtime follows the first
  outgoing edge it finds.
- Additional Entry or End nodes are not special-cased at runtime. With only one route, nodes
  off that route are unreachable; Validate Graph warns about duplicate Entry nodes and about
  every unreachable node. See 5.2 for authoring guidance.

### 4.6 Connection points for your game

Edit these files to add your values:

- `Runtime/Core/DialogueEventEnums.cs` defines `DialogueNpcId`, `DialogueEventType` and
  `DialogueNpcState`. Each starts with `None = 0`. Add your real NPCs, events and states here;
  the package cannot know them.
- The NPC enum will be wired to actual NPC entities later. Until then, trigger with the enum
  value that represents that entity.
- Catalog entries reference these enum values directly, so adding values is enough to make them
  authorable; no other package file needs editing.

Effects and gameplay state are game concerns. Subscribe to `Dialogue.DialogueEnded` and apply
flags, quest changes, stat updates, or follow-up triggers there. The package deliberately does
not expose effects, conditions or a global dialogue state container.

---

## 5. Authoring

Open **Custom Tools > Dialogue Graph Editor** and select a `DialogueGraph` asset. The left pane
is the GraphView canvas, the right pane is the node inspector. The window follows the currently
selected graph.

- **Add nodes:** right-click the canvas (Add Line Node / Add Entry Node / Add End Node).
- **Connect:** drag from an output port to an input port. Inputs accept multiple connections
  (fan-in); outputs accept one connection (single-fanout). This is what makes a graph a chain.
- **Move:** drag nodes; positions persist in the asset.
- **Clipboard:** Ctrl+C / Ctrl+X / Ctrl+V / Ctrl+D (or the context menu). Copies deep-clone
  nodes and their internal edges; GUIDs are remapped on paste.
- **Layout Graph:** BFS depth layout from Entry nodes; unreachable islands are placed to the
  right.
- **Validate Graph:** logs warnings for dangling edges, missing or duplicate Entry nodes,
  empty line messages, Entries without an outgoing edge, and unreachable nodes.
- **Line data:** the node inspector edits **Message** and **Delay** only.
- **Entry / End data:** descriptive only; they carry no data.
- Graphs and lines carry only message text and timing; there are no authoring sections for
  branching, conditions or effects.

### 5.1 Node reference

| Node  | Purpose | Data |
|-------|---------|------|
| Entry | Start marker. Exactly one is expected; the first Entry starts playback. | none |
| Line  | Delivers one line, then follows its single outgoing edge. | `text`, `delaySeconds` |
| End   | Ends the dialogue when playback reaches it. | none |

`DialogueNodeKind` uses `Line = 0`, `Entry = 3`, `End = 4`. Values 1 and 2 are deliberately
skipped legacy values from earlier package versions, and old assets may still contain them.
Do not reuse or renumber those values.

### 5.2 Entry, End and the single route

Because each node has at most one outgoing edge, a graph has exactly one route from its Entry:

- **Multiple Entry nodes:** the first Entry (in node list order) starts playback. A second
  Entry node is ignored at runtime and is unreachable; Validate Graph warns about the duplicate
  Entry and about the unreachable node.
- **Multiple End nodes:** an End node is not special-cased. Any End node on the single route
  ends the dialogue. Additional End nodes off that route are unreachable; there is no separate
  "multiple End" warning, but Validate Graph's reachability check reports them.
- **Line with no outgoing edge:** ends the dialogue. An End node is optional but recommended so
  the intent is visible and validation can distinguish it from an accidental dangling end.
- **Graph with no Line nodes:** it must still start at an Entry node. Playback enters Entry,
  advances to End (or a missing edge) and ends immediately with no lines delivered. If there is
  no Entry node, runtime falls back to the first node in the list, but Validate Graph warns
  about the missing Entry.
- Branching is not authored inside one graph. To branch, register multiple graphs for different
  NPC/event/state combinations and let the game raise the appropriate trigger (see 5.3 and 8).

### 5.3 Authoring catalogs

Create the catalog asset via **Assets > Create > Dialogue System > Dialogue Catalog**, then open
**Custom Tools > Dialogue Catalog Editor**. The window:

- has a catalog asset picker and also auto-follows Project-window selection when a
  `DialogueCatalog` is selected (selecting a different asset type leaves the current target in
  place);
- renders one collapsible section per NPC group, with a graph/event/state row per entry,
  **Duplicate** and remove buttons, and **+ Add Group** / **+ Add Entry** actions;
- revalidates live on every repaint and shows a HelpBox for: an NPC group using `None` (which
  can never match because triggers reject `DialogueNpcId.None`), duplicate NPC values (only the
  first group is used), groups with zero entries, entries with no graph assigned, and the same
  graph registered twice in one exact event/state set.

`None`/`None` event and state values are valid. All mutations go through Unity's
`SerializedObject`, so Undo/Redo and asset persistence work normally.

Selecting a graph or catalog asset in the Project window shows a stub Inspector with a help box
and an **Open Graph Editor** / **Open Catalog Editor** button. Authoring decisions happen in the
dedicated windows; the Inspectors stay free of the authoring UI.

---

## 6. API reference

### 6.1 `Dialogue` (static facade)

```csharp
public static event Action<DialogueLineEventArgs> LineReceived;
public static event Action<DialogueEndedEventArgs> DialogueEnded;   // recommended end signal
public static bool IsDialogueActive { get; }
public static DialogueNpcId ActiveTrigger { get; }                  // None when idle
public static void Trigger(
    DialogueNpcId npc,
    DialogueEventType eventType = DialogueEventType.None,
    DialogueNpcState npcState = DialogueNpcState.None);
```

`Trigger` warns and returns when no director is active or when `npc == DialogueNpcId.None`.
Otherwise it queues the enum triple for the director's next `Update`.

### 6.2 Event payloads

`DialogueLineEventArgs`:

| Field | Meaning |
|-------|---------|
| `Text` | The delivered line text. |
| `TriggerNpc` | `DialogueNpcId` of the NPC whose dialogue is playing. |
| `EventType` | `DialogueEventType` from the raising trigger event. |
| `NpcState` | `DialogueNpcState` from the raising trigger event. |
| `LineNumber` | 0-based delivered-line index within the dialogue. |

`DialogueEndedEventArgs`:

| Field | Meaning |
|-------|---------|
| `TriggerNpc` | `DialogueNpcId` of the NPC whose dialogue ended. |
| `EventType` | `DialogueEventType` from the raising trigger event. |
| `NpcState` | `DialogueNpcState` from the raising trigger event. |
| `Completed` | `true` when the graph finished (after the last line); `false` when aborted, for example by a `Replace` policy. |
| `Graph` | The graph that was playing (also on `DialogueLineEventArgs`), for its `payload`, name or `isOneShot`. |

`DialogueEnded` fires after the last line is delivered or on abort. It is the recommended way
for listeners to know a dialogue ended.

### 6.3 `DialogueDirector` (component)

| Field | Default | Meaning |
|-------|---------|---------|
| `catalog` | none | The `DialogueCatalog` asset (NPC groups and play-once bags) to use. |
| `defaultLineDelaySeconds` | 1.2 | Used when a line's delay is negative. |
| `policyOnNewTrigger` | `Queue` | See 4.3. |
| `queuedDialogueDelaySeconds` | 1.5 | `FinishThenQueue` only: pause before a queued, non-priority dialogue starts. |
| `persistAcrossScenes` | true | `DontDestroyOnLoad`; duplicate directors are destroyed. |
| `verboseLogging` | false | Logs selection and policy details. |

`DialogueDirector.Instance` is a static singleton. The facade routes triggers to it. Only one
director may exist.

### 6.4 `DialogueGraph` / `DialogueCatalog` assets

`DialogueGraph` exposes `GetNode(guid)`, `GetEntryNode()` and `GetNextNode(guid)`.
`GetEntryNode` returns the first Entry node, falling back to the first non-null node when no
Entry exists (validation warns about that fallback). `GetNextNode` returns the target of the
node's first outgoing edge.

`DialogueCatalog` exposes `SelectGraph(npcId, eventType, npcState)`,
`HasRule(npcId, eventType, npcState)` and `ResetPlayed()`. `SelectGraph` returns `null` when
nothing matches; `HasRule` reports whether any authored entry matches and deliberately ignores
the play-once state. Runtime log messages identify graphs by asset name.

These are plain data helpers; mutation is editor-only (`DialogueGraphEditorExtensions` in the
editor assembly).

---

## 7. Architecture map (for developers and AI agents)

```
Runtime/ (assembly DialogueSystem.Runtime, namespace DialogueSystem)
  Core/DialogueEnums.cs             node/policy enums (Line=0, Entry=3, End=4; holes kept)
  Core/DialogueEventEnums.cs        game-extended DialogueNpcId / DialogueEventType / DialogueNpcState enums
  Core/DialogueLine.cs              text + delaySeconds
  Core/DialogueNodeData.cs          DialogueNodeData, DialogueEdgeData
  Core/DialogueGraph.cs             ScriptableObject: nodes, edges; lookup helpers only
  Core/DialogueCatalog.cs           ScriptableObject: NPC groups/entries; SelectGraph/HasRule/ResetPlayed + session-only play-once bags
  DialogueEventData.cs              DialogueLineEventArgs, DialogueEndedEventArgs
  Dialogue.cs                       static facade (trigger in, line/ended out)
  DialogueRunner.cs                 plain C# playback state machine (one dialogue)
  DialogueDirector.cs               MonoBehaviour: catalog, trigger pipeline, policy, events

Editor/ (assembly DialogueSystem.Editor, namespace DialogueSystem.EditorTools)
  DialogueCatalogWindow.cs          standalone catalog authoring window: groups, entry rows, validation
  DialogueAssetInspectors.cs        stub graph Inspector (help box + Open Editor)
  DialogueCatalogEditor.cs          stub catalog Inspector (help box + Open Editor)
  DialogueGraphEditorExtensions.cs  editor-only AddNode/RemoveNode/Connect/Disconnect
  DialogueNodeView.cs               GraphView node view (single output port, previews)
  DialogueGraphView.cs              GraphView shell (clipboard intents, port rules, selection event)
  DialogueGraphEditor.cs            EditorWindow: canvas, inspector, clipboard, layout, validation

package.json, README.md
```

Key invariants (do not break them when extending):

1. **Runtime has no editor/UI dependencies.** No `UnityEditor`, TMP, uGUI, Addressables in
   `Runtime/`. Editor-only mutation belongs in the editor assembly.
2. **Never start or resolve anything synchronously inside runner callbacks.** Triggers are
   queued by the facade and drained in `Update`; `StartGraph` is only called from the director's
   drain path. This prevents re-entrancy while the runner is mid-walk.
3. **One active dialogue.** One director, one runner. Queued triggers are re-selected when
   they start, never at enqueue time.
4. **The game contract stays small.** One trigger channel in; `LineReceived` and
   `DialogueEnded` out. Adding new mandatory communication paths (extra events the game must
   subscribe to, required callbacks) is a breaking design change.
5. **Serialization is explicit and asset-only.** `DialogueGraph`, `DialogueCatalog`, and their
   nested node/entry/line types are plain `[System.Serializable]` classes of public fields,
   lists, primitives and Unity asset references. They are Unity-serialized - never hand-edit
   their `.asset` YAML. Do not add polymorphism (interfaces, subclass dispatch) to the data
   model. A `[System.NonSerialized]` runtime-only dictionary (the catalog's play-once bag) is
   allowed precisely because Unity never serializes it; do not add dictionaries to the
   serialized payload, and do not stash graph references in the asset outside the entry list.
6. **Node kinds are switched in several places.** If you add one, update:
   `DialogueRunner.ProcessCurrentNode` (playback), `DialogueGraphEditorExtensions.AddNode`
   (creation), `DialogueNodeView` (ports + preview), `DialogueGraphEditor` (inspector +
   validation), and this README. The numeric values `Line = 0`, `Entry = 3`, `End = 4` are part
   of the asset format; values 1 and 2 are reserved legacy holes and must not be reused.
7. **Never hand-edit `.asset` / `.prefab` / `.unity` files.** Use the graph editor, the catalog
   window and the Inspector. Graph positions and all node data live in the ScriptableObject
   asset.

---

## 8. Design decisions and non-goals

- **No UI, audio, input or typing effect.** Lines are text plus timing, delivered by one event.
  Presentation belongs to the game.
- **No save system and no persisted played state.** Graphs are referenced by the catalog asset
  directly. The play-once bag is session-only; call `ResetPlayed()` on a new game or load when
  every graph should become eligible again.
- **Triggers are stateless enum triples, not latched state.** There is no global dialogue state
  container and no trigger history. The game owns flags, numbers, quest state and other
  gameplay data; the package only routes an exact (NPC, event type, state) request.
- **Graph selection is a play-once bag, not weighted random.** Every distinct graph in an exact
  (npc, event type, NPC state) set plays once before the set resets, so all authored variants
  are seen without weights or condition clauses. Played state is runtime-only.
- **No Choice or Wait nodes; branching happens across dialogues, not inside one graph.** A graph
  is a linear chain. To branch, the game registers multiple graphs in the catalog for different
  NPC/event/state combinations and raises the appropriate trigger based on its own logic. Waits,
  conditions and decision-making are game concerns; the package does not park a graph.
- **`DialogueEnded` is a first-class completion signal.** `Completed` distinguishes a normal
  finish from an abort, so listeners do not have to infer completion from the last line.
- **Single-fanout graphs have one route.** Multiple Entry/End nodes therefore leave nodes
  unreachable, and Validate Graph reports them; author one Entry and put End nodes on the single
  route.
- **A trigger that arrives mid-dialogue follows the policy.** `Queue`, `Ignore` and `Replace`
  are explicit; there is no implicit trigger consumption by a wait, because waits no longer
  exist.
- **Trigger drains are budgeted.** At most 256 triggers are processed per frame; a larger flood
  drops the remaining triggers with a warning instead of hanging the frame.

## 9. Troubleshooting

| Symptom | Check |
|---------|-------|
| Trigger fired but nothing played | The NPC is not in the catalog, or the event-type/state enum pair does not match any entry. Matching is exact enum equality and the first group with that NPC wins. Bare `Trigger(npc)` maps to `None`/`None` and only matches entries authored with `None`/`None`. |
| Nothing happens on `Trigger` at all | Is a `DialogueDirector` in the scene (its `Awake` must run; `persistAcrossScenes` keeps it alive)? Is `catalog` assigned? Is the trigger NPC a non-`None` value that has a group? |
| Warning "No DialogueDirector is active" | The trigger ran before the director's `Awake`, or no director exists. |
| Trigger warns "without an NPC" / is ignored | `DialogueNpcId.None` is rejected by the facade and by `EnqueueTrigger`; use a real NPC enum value. |
| Dialogue starts but no lines | The graph has no Line nodes, its Entry has no outgoing edge, or its Lines are outside the single route. A graph with no lines must still start at Entry; playback then ends immediately. Run Validate Graph. |
| Second Entry node is ignored | The first Entry (in node list order) starts playback; additional Entry nodes are unreachable. Validate Graph warns about both the duplicate Entry and the unreachable node. |
| Several End nodes, but only one seems to work | With single-fanout outputs there is only one route. End nodes off that route are unreachable; Validate Graph reports them. |
| Dialogue ends earlier than expected | A Line with no outgoing edge also ends the dialogue by design, or the step budget detected a loop. Add an End node on the route for clarity and run Validate Graph. |
| A trigger is ignored mid-dialogue | `policyOnNewTrigger` is `Ignore`, or no catalog entry matches. |
| A graph seems skipped or repeats unexpectedly | Play-once bags play each graph of an exact (npc, event type, NPC state) set once in authored order, then pick randomly; spent one-shots are skipped for good. Call `ResetPlayed()` on the catalog asset on a new game/load if played state should start fresh. |
| Can't connect ports in the editor | Use the package editor window (Custom Tools > Dialogue Graph Editor); its GraphView overrides port compatibility for Unity 6. Inputs are multi, outputs are single. |
| Played dialogues unexpectedly persist between play sessions | Domain reload is disabled; call `ResetPlayed()` on the catalog asset on game start. |
