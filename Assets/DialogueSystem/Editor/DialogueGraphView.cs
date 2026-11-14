using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// GraphView shell with zoom/pan/box-select and a public SelectionChanged C# event
    /// (Unity 6's GraphView exposes only a read-only selection list - selection changes
    /// are dispatched by overriding the AddToSelection/RemoveFromSelection virtuals).
    /// Clipboard intents follow the same pattern: the default Cut/Copy/Paste/Duplicate
    /// pipeline round-trips through GraphView's element serialization, which carries no
    /// payload for our custom nodes - so the view forwards intents to the editor window,
    /// which owns all data mutation.
    /// </summary>
    public class DialogueGraphView : GraphView
    {
        public event System.Action SelectionChanged;

        // clipboard intents - raised from the trickle-down ExecuteCommandEvent handler
        // (Ctrl+X/C/V/D) and from the context menu actions rebuilt in
        // BuildContextualMenu; consumed by DialogueGraphEditor
        public event System.Action CutRequested;
        public event System.Action CopyRequested;
        public event System.Action PasteRequested;
        public event System.Action DuplicateRequested;

        // paste enablement is owned by the window (the in-memory clipboard lives there);
        // the view only consults it in canPaste and in the menu status callback
        public System.Func<bool> CanPasteHandler;

        // NOTE: UnityEngine.UIElements.EventCommandNames is INTERNAL in Unity 6 - the
        //       clipboard command names arrive as plain strings on
        //       ExecuteCommandEvent.commandName, so they are literal constants here
        private const string CommandCut = "Cut";
        private const string CommandCopy = "Copy";
        private const string CommandPaste = "Paste";
        private const string CommandDuplicate = "Duplicate";

        public DialogueGraphView()
        {
            style.flexGrow = 1f;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            // Ctrl+X/C/V/D arrive as ExecuteCommandEvent, NOT KeyDownEvent (Unity 6
            // GraphView handles them in its bubble-phase OnExecuteCommand, which for
            // Cut/Copy/Duplicate calls the non-virtual CutSelectionCallback/
            // CopySelectionCallback/DuplicateSelectionCallback and for Paste calls
            // PasteCallback - all useless for our custom nodes). Intercept trickle-down
            // and forward the intents instead, stopping the event so the default
            // element-serialization pipeline never runs.
            this.RegisterCallback<ExecuteCommandEvent>(OnExecuteClipboardCommand, TrickleDown.TrickleDown);
            // NOTE: no ValidateCommandEvent handler is registered - GraphView's own
            //       OnValidateCommand (registered in its constructor) consults the
            //       overridable canCutSelection/canCopySelection/canPaste/
            //       canDuplicateSelection virtuals, so shortcut and menu enablement
            //       already reflect the overrides below.
        }

        // clipboard enablement: the can* virtuals are the ONLY overridable part of the
        // default clipboard pipeline in Unity 6 - the callbacks themselves are
        // non-virtual and unusable, so the intents above carry the real work
        protected override bool canCutSelection => HasNodeSelection();
        protected override bool canCopySelection => HasNodeSelection();
        protected override bool canDuplicateSelection => HasNodeSelection();
        protected override bool canPaste => CanPasteHandler != null && CanPasteHandler();

        private bool HasNodeSelection()
        {
            foreach(ISelectable selectable in selection)
            {
                if(selectable is DialogueNodeView) return true;
            }
            return false;
        }

        private void OnExecuteClipboardCommand(ExecuteCommandEvent evt)
        {
            if(evt.commandName == CommandCut)
            {
                if(!canCutSelection) return;
                CutRequested?.Invoke();
                evt.StopImmediatePropagation();
            }
            else if(evt.commandName == CommandCopy)
            {
                if(!canCopySelection) return;
                CopyRequested?.Invoke();
                evt.StopImmediatePropagation();
            }
            else if(evt.commandName == CommandPaste)
            {
                if(!canPaste) return;
                PasteRequested?.Invoke();
                evt.StopImmediatePropagation();
            }
            else if(evt.commandName == CommandDuplicate)
            {
                if(!canDuplicateSelection) return;
                DuplicateRequested?.Invoke();
                evt.StopImmediatePropagation();
            }
            // unhandled commands (Delete, Select All, ...) fall through to the base handler
        }

        /// <summary>
        /// The base appends the default Cut/Copy/Paste/Duplicate actions, wired to the
        /// unusable element-serialization pipeline. Remove them and re-insert our own
        /// actions at the same positions, wired to the intent events. The window's own
        /// OnBuildContextualMenu callback still appends the Add-node / Layout / Validate
        /// entries afterwards.
        /// </summary>
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            // NOTE: Unity 6 removed the nested DropdownMenu.MenuItem type (items are the
            //       top-level DropdownMenuItem), and only DropdownMenuAction exposes the
            //       public `name` the defaults are keyed by - match through a cast, not
            //       ToString() (which yields the type name). .ToList() normalizes the
            //       collection shape returned by MenuItems().
            List<DropdownMenuItem> items = evt.menu.MenuItems().ToList();
            int cutIndex = items.FindIndex(item => item is DropdownMenuAction action && action.name == "Cut");
            int copyIndex = items.FindIndex(item => item is DropdownMenuAction action && action.name == "Copy");
            int pasteIndex = items.FindIndex(item => item is DropdownMenuAction action && action.name == "Paste");
            int duplicateIndex = items.FindIndex(item => item is DropdownMenuAction action && action.name == "Duplicate");

            // nothing to replace (base variant without the clipboard actions) - keep defaults
            if(cutIndex < 0 && copyIndex < 0 && pasteIndex < 0 && duplicateIndex < 0) return;

            // remove from the highest index to the lowest so earlier indices stay valid
            int[] removeAt = { duplicateIndex, pasteIndex, copyIndex, cutIndex };
            for(int i = 0; i < removeAt.Length; i++)
            {
                if(removeAt[i] >= 0) evt.menu.RemoveItemAt(removeAt[i]);
            }

            // DropdownMenuAction callbacks are private, so the defaults cannot be retargeted
            // in place - insert fresh actions at the slot where "Cut" used to sit, keeping
            // the four grouped where the defaults were
            int insertAt = cutIndex >= 0 ? cutIndex : 0;
            evt.menu.InsertAction(insertAt, "Cut", _ => CutRequested?.Invoke(), _ => canCutSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.InsertAction(insertAt + 1, "Copy", _ => CopyRequested?.Invoke(), _ => canCopySelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.InsertAction(insertAt + 2, "Paste", _ => PasteRequested?.Invoke(), _ => canPaste ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.InsertAction(insertAt + 3, "Duplicate", _ => DuplicateRequested?.Invoke(), _ => canDuplicateSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        // Unity 6 ships no default NodeAdapter for PortSource<T>, so the stock
        // GetCompatiblePorts (adapter type lookup) never matches any port pair -
        // every dragged edge would show as incompatible. Like Unity's official
        // SimpleGraphView sample, drop the adapter check: any output may connect
        // to any input on a different node.
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            return ports.ToList().Where(port =>
                port != startPort &&
                port.node != startPort.node &&
                port.direction != startPort.direction).ToList();
        }

        public override void AddToSelection(ISelectable selectable)
        {
            base.AddToSelection(selectable);
            SelectionChanged?.Invoke();
        }

        public override void RemoveFromSelection(ISelectable selectable)
        {
            base.RemoveFromSelection(selectable);
            SelectionChanged?.Invoke();
        }

        public override void ClearSelection()
        {
            base.ClearSelection();
            SelectionChanged?.Invoke();
        }
    }
}
