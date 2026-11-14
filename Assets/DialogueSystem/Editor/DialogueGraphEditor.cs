using System;
using System.Collections.Generic;
using System.Linq;
using DialogueSystem;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// GraphView-based authoring tool for <see cref="DialogueGraph"/> assets.
    /// The canvas is a pure view over the data model: every node view maps back to a node
    /// GUID, and the inspector pane edits the node data directly. All GraphView touch points
    /// live in this file.
    /// </summary>
    public class DialogueGraphEditor : EditorWindow
    {
        private DialogueGraph targetGraph;
        private DialogueGraphView graphView;
        private IMGUIContainer inspectorContainer;
        private DialogueNodeData selectedNodeData;
        private Vector2 inspectorScroll;

        // node GUID -> view map, rebuilt wholesale by RebuildGraphView(); lets inspector
        // commits refresh a single node's preview in place
        private readonly Dictionary<string, DialogueNodeView> nodeViews = new Dictionary<string, DialogueNodeView>();

        /// <summary>
        /// In-window clipboard: deep clones of selected nodes plus the internal edges
        /// between them, with old-to-new GUID remaps prepared at paste.
        /// </summary>
        private class NodeClipboard
        {
            public List<DialogueNodeData> nodes = new List<DialogueNodeData>();
            public List<DialogueEdgeData> edges = new List<DialogueEdgeData>();
        }

        // clipboard is scoped to one graph: cleared when targetGraph changes or the
        // window closes; never survives a domain reload
        private NodeClipboard clipboard;

        [MenuItem("Custom Tools/Dialogue Graph Editor")]
        public static void OpenWindow()
        {
            GetWindow<DialogueGraphEditor>("Dialogue Graph Editor");
        }

        private void OnEnable()
        {
            // NOTE: Unity re-invokes OnEnable on a surviving window after a domain reload
            //       (e.g. a recompile or an AssetDatabase.Refresh), and the native visual
            //       tree persists across that reload. Without clearing, a second split
            //       view stacks under the first and the window renders doubled (with the
            //       stale graph view in the top half). Clear is a no-op when the tree is
            //       already empty.
            rootVisualElement.Clear();

            var splitView = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(splitView);

            graphView = new DialogueGraphView();
            graphView.graphViewChanged = OnGraphViewChanged;
            graphView.SelectionChanged += OnGraphViewSelectionChanged;
            graphView.CutRequested += OnCutRequested;
            graphView.CopyRequested += OnCopyRequested;
            graphView.PasteRequested += OnPasteRequested;
            graphView.DuplicateRequested += OnDuplicateRequested;
            graphView.CanPasteHandler = () => clipboard != null && clipboard.nodes.Count > 0 && targetGraph != null;
            graphView.RegisterCallback<ContextualMenuPopulateEvent>(OnBuildContextualMenu);
            splitView.Add(graphView);

            inspectorContainer = new IMGUIContainer(DrawInspector);
            splitView.Add(inspectorContainer);

            // NOTE: OnEnable can run again without a preceding OnDisable, so unsubscribe
            //       before subscribing to keep the static event single-subscribed
            Selection.selectionChanged -= OnSelectionChanged;
            Selection.selectionChanged += OnSelectionChanged;
            // undo/redo mutates the graph asset in memory - the canvas must be rebuilt or
            // the change is invisible (the graph operations record Undo)
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            OnSelectionChanged();
        }

        private void OnDisable()
        {
            if(graphView != null)
            {
                graphView.SelectionChanged -= OnGraphViewSelectionChanged;
                graphView.CutRequested -= OnCutRequested;
                graphView.CopyRequested -= OnCopyRequested;
                graphView.PasteRequested -= OnPasteRequested;
                graphView.DuplicateRequested -= OnDuplicateRequested;
                graphView.CanPasteHandler = null;
            }
            Selection.selectionChanged -= OnSelectionChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            nodeViews.Clear();
            clipboard = null;
        }

        /// <summary>
        /// Undo/redo restores the graph asset's serialized state, but the GraphView is a
        /// separate visual tree that must be rebuilt to reflect it - otherwise Ctrl+Z
        /// looks like a no-op (the nodes stay on the canvas).
        /// </summary>
        private void OnUndoRedoPerformed()
        {
            if(targetGraph == null) return;

            // the selected node may have been removed by the undo (e.g. undoing a paste)
            if(selectedNodeData != null && !string.IsNullOrEmpty(selectedNodeData.guid)
                && targetGraph.GetNode(selectedNodeData.guid) == null)
            {
                selectedNodeData = null;
            }

            RebuildGraphView();
            inspectorContainer?.MarkDirtyRepaint();
        }

        private void OnSelectionChanged()
        {
            DialogueGraph newTarget = Selection.activeObject as DialogueGraph;
            if(newTarget == null || newTarget == targetGraph) return;

            targetGraph = newTarget;
            clipboard = null; // clipboard is per-graph
            selectedNodeData = null;
            bool laidOut = EnsureDefaultLayout();
            RebuildGraphView();
            if(laidOut) FrameGraphContent();
            inspectorContainer?.MarkDirtyRepaint();
        }

        #region Graph View Lifecycle

        private void RebuildGraphView()
        {
            if(graphView == null) return;

            nodeViews.Clear();

            foreach(GraphElement element in graphView.graphElements.ToList())
            {
                graphView.RemoveElement(element);
            }

            if(targetGraph == null) return;

            if(targetGraph.nodes != null)
            {
                foreach(DialogueNodeData node in targetGraph.nodes)
                {
                    if(node == null || string.IsNullOrEmpty(node.guid)) continue;
                    DialogueNodeView view = new DialogueNodeView(node);
                    nodeViews[node.guid] = view;
                    graphView.AddElement(view);
                }
            }

            if(targetGraph.edges != null)
            {
                foreach(DialogueEdgeData edge in targetGraph.edges)
                {
                    if(edge == null) continue;
                    if(!nodeViews.TryGetValue(edge.fromNodeGuid, out DialogueNodeView fromView)) continue;
                    if(!nodeViews.TryGetValue(edge.toNodeGuid, out DialogueNodeView toView)) continue;

                    Port outputPort = fromView.OutputPort;

                    if(outputPort == null || toView.InputPort == null) continue;

                    Edge viewEdge = outputPort.ConnectTo(toView.InputPort);
                    viewEdge.userData = edge;
                    graphView.AddElement(viewEdge);
                }
            }

            inspectorContainer?.MarkDirtyRepaint();
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange changes)
        {
            // NOTE: new edges the user just dragged are reported here - Port.edgeConnector
            //       is read-only in Unity 6, so the default DefaultEdgeConnectorListener
            //       routes new edges into edgesToCreate instead of a custom
            //       IEdgeConnectorListener
            if(changes.edgesToCreate != null && targetGraph != null)
            {
                foreach(Edge edge in changes.edgesToCreate)
                {
                    if(edge == null || edge.output == null || edge.input == null) continue;
                    if(!(edge.output.node is DialogueNodeView fromView)) continue;
                    if(!(edge.input.node is DialogueNodeView toView)) continue;

                    Undo.RecordObject(targetGraph, "Connect dialogue nodes");
                    targetGraph.Connect(fromView.NodeGuid, toView.NodeGuid);
                    // Bind the new visual edge to its data edge so deleting it from the canvas
                    // disconnects the right edge instead of leaving a hidden one in the asset.
                    DialogueEdgeData createdEdge = FindEdge(fromView.NodeGuid, toView.NodeGuid);
                    if(createdEdge != null) edge.userData = createdEdge;
                    PersistGraph();
                }
            }

            // dragged node positions persist with the graph asset, so layouts survive reopening
            if(changes.movedElements != null && targetGraph != null)
            {
                bool anyMoved = false;
                foreach(GraphElement element in changes.movedElements)
                {
                    if(element is DialogueNodeView nodeView && nodeView.NodeData != null)
                    {
                        nodeView.NodeData.editorPosition = nodeView.GetPosition().position;
                        anyMoved = true;
                    }
                }

                if(anyMoved) PersistGraph();
            }

            if(changes.elementsToRemove != null)
            {
                foreach(GraphElement element in changes.elementsToRemove)
                {
                    if(element is DialogueNodeView nodeView)
                    {
                        if(selectedNodeData != null && selectedNodeData.guid == nodeView.NodeGuid) selectedNodeData = null;

                        if(targetGraph != null)
                        {
                            Undo.RecordObject(targetGraph, "Remove dialogue node");
                            targetGraph.RemoveNode(nodeView.NodeGuid);
                            PersistGraph();
                        }

                        EditorApplication.delayCall += RebuildGraphView;
                    }
                    else if(element is Edge edge && edge.userData is DialogueEdgeData dataEdge)
                    {
                        if(targetGraph != null)
                        {
                            Undo.RecordObject(targetGraph, "Disconnect dialogue nodes");
                            targetGraph.Disconnect(dataEdge);
                            PersistGraph();
                        }

                        EditorApplication.delayCall += RebuildGraphView;
                    }
                }
            }

            return changes;
        }

        private void OnGraphViewSelectionChanged()
        {
            selectedNodeData = null;

            foreach(ISelectable selectable in graphView.selection)
            {
                if(selectable is DialogueNodeView nodeView)
                {
                    selectedNodeData = nodeView.NodeData;
                    break;
                }
            }

            inspectorContainer?.MarkDirtyRepaint();
        }

        private void OnBuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if(targetGraph == null) return;

            evt.menu.AppendAction("Add Line Node", _ => AddNode(DialogueNodeKind.Line));
            evt.menu.AppendAction("Add Entry Node", _ => AddNode(DialogueNodeKind.Entry));
            evt.menu.AppendAction("Add End Node", _ => AddNode(DialogueNodeKind.End));
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Layout Graph", _ => { LayoutGraph(); RebuildGraphView(); FrameGraphContent(); });
            evt.menu.AppendAction("Validate Graph", _ => ValidateGraph());
        }

        /// <summary>
        /// Auto-arranges nodes in depth layers (BFS from entry nodes); islands unreachable
        /// from any entry are placed to the right of the main flow. Positions are written
        /// back to the node data and persisted.
        /// </summary>
        private void LayoutGraph()
        {
            if(targetGraph == null || targetGraph.nodes == null || targetGraph.nodes.Count == 0) return;

            Undo.RecordObject(targetGraph, "Layout dialogue graph");

            const float columnSpacing = 280f;
            const float rowSpacing = 140f;
            const float margin = 40f;

            Dictionary<string, int> depths = new Dictionary<string, int>();
            Queue<DialogueNodeData> frontier = new Queue<DialogueNodeData>();

            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null || string.IsNullOrEmpty(node.guid)) continue;
                if(node.kind != DialogueNodeKind.Entry) continue;

                depths[node.guid] = 0;
                frontier.Enqueue(node);
            }

            while(frontier.Count > 0)
            {
                DialogueNodeData current = frontier.Dequeue();
                if(targetGraph.edges == null) continue;

                foreach(DialogueEdgeData edge in targetGraph.edges)
                {
                    if(edge == null || edge.fromNodeGuid != current.guid) continue;

                    DialogueNodeData next = targetGraph.GetNode(edge.toNodeGuid);
                    if(next == null || depths.ContainsKey(next.guid)) continue;

                    depths[next.guid] = depths[current.guid] + 1;
                    frontier.Enqueue(next);
                }
            }

            int maxDepth = 0;
            foreach(int depth in depths.Values) maxDepth = Mathf.Max(maxDepth, depth);

            int[] rowCounters = new int[maxDepth + 1];
            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null || string.IsNullOrEmpty(node.guid) || !depths.ContainsKey(node.guid)) continue;

                int depth = depths[node.guid];
                node.editorPosition = new Vector2(margin + depth * columnSpacing, margin + rowCounters[depth] * rowSpacing);
                rowCounters[depth]++;
            }

            float islandX = margin + (maxDepth + 2) * columnSpacing;
            int islandRow = 0;

            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null || string.IsNullOrEmpty(node.guid) || depths.ContainsKey(node.guid)) continue;

                node.editorPosition = new Vector2(islandX, margin + islandRow * rowSpacing);
                islandRow++;
            }

            PersistGraph();
        }

        /// <summary>
        /// Freshly-authored nodes sit at the origin - run the default layout when any two
        /// nodes overlap. Called on graph open, so manually arranged layouts are untouched.
        /// Returns true when the default layout actually ran.
        /// </summary>
        private bool EnsureDefaultLayout()
        {
            if(targetGraph == null || targetGraph.nodes == null || targetGraph.nodes.Count < 2) return false;

            for(int i = 0; i < targetGraph.nodes.Count; i++)
            {
                if(targetGraph.nodes[i] == null) continue;

                for(int j = i + 1; j < targetGraph.nodes.Count; j++)
                {
                    if(targetGraph.nodes[j] == null) continue;

                    if(Vector2.Distance(targetGraph.nodes[i].editorPosition, targetGraph.nodes[j].editorPosition) < 1f)
                    {
                        LayoutGraph();
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Frames all graph content in the center of the viewport. Bounds come from the
        /// data model (node editorPosition) plus laid-out/placeholder sizes, so - unlike
        /// FrameAll()/CalculateRectToFitAll() - framing does not depend on a measured
        /// layout pass and can be applied synchronously. The transform is produced by
        /// Unity's own GraphView.CalculateFrameTransform, so the fit/centering matches
        /// FrameAll exactly.
        /// </summary>
        private void FrameGraphContent()
        {
            if(graphView == null || targetGraph == null || nodeViews.Count == 0) return;

            ApplyGraphFrame();

            // re-apply once on the next tick so pre-layout placeholder sizes are replaced
            // by the real laid-out sizes
            graphView.schedule.Execute(ApplyGraphFrame);
        }

        /// <summary>
        /// True when a float is neither NaN nor +/-Infinity. Layout sizes read as NaN
        /// before an element has been laid out, and NaN fails every comparison, so a plain
        /// `value &lt; threshold` test silently lets NaN through.
        /// </summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void ApplyGraphFrame()
        {
            if(graphView == null) return;

            // nominal node footprint, ~= one layout grid cell (columnSpacing 280 x rowSpacing 140)
            const float fallbackWidth = 240f;
            const float fallbackHeight = 120f;

            bool hasBounds = false;
            Rect contentRect = new Rect();

            foreach(DialogueNodeView view in nodeViews.Values)
            {
                if(view == null) continue;

                // use the data-model position (the exact content-space coordinate
                // LayoutGraph writes) plus the element's laid-out size; the size is
                // NaN/zero until the layout pass has run, so fall back to a nominal
                // footprint
                Vector2 nodePosition = view.NodeData != null ? view.NodeData.editorPosition : Vector2.zero;
                if(!IsFinite(nodePosition.x) || !IsFinite(nodePosition.y)) nodePosition = Vector2.zero;

                Vector2 nodeSize = view.layout.size;
                if(!IsFinite(nodeSize.x) || nodeSize.x <= 1f) nodeSize.x = fallbackWidth;
                if(!IsFinite(nodeSize.y) || nodeSize.y <= 1f) nodeSize.y = fallbackHeight;
                Rect nodeRect = new Rect(nodePosition, nodeSize);

                if(!hasBounds)
                {
                    contentRect = nodeRect;
                    hasBounds = true;
                }
                else
                {
                    contentRect = Rect.MinMaxRect(
                        Mathf.Min(contentRect.xMin, nodeRect.xMin),
                        Mathf.Min(contentRect.yMin, nodeRect.yMin),
                        Mathf.Max(contentRect.xMax, nodeRect.xMax),
                        Mathf.Max(contentRect.yMax, nodeRect.yMax));
                }
            }

            // a non-finite rect would make CalculateFrameTransform emit NaN, which
            // UpdateViewTransform rejects outright (silent no-op)
            if(!hasBounds || !IsFinite(contentRect.x) || !IsFinite(contentRect.y)
                || !IsFinite(contentRect.width) || !IsFinite(contentRect.height)) return;

            // clientRect is the GraphView's own layout rect, exactly what Frame() passes
            Rect viewport = graphView.layout;
            if(!IsFinite(viewport.width) || !IsFinite(viewport.height) || viewport.width <= 1f || viewport.height <= 1f) return;

            GraphView.CalculateFrameTransform(contentRect, viewport, 30, out Vector3 translation, out Vector3 scaling);
            graphView.UpdateViewTransform(translation, scaling);
            // the editor panel does not always repaint on a pure transform change
            graphView.MarkDirtyRepaint();
        }

        private void AddNode(DialogueNodeKind kind)
        {
            if(targetGraph == null) return;

            Undo.RecordObject(targetGraph, "Add dialogue node");
            DialogueNodeData node = targetGraph.AddNode(kind);
            node.editorPosition = new Vector2(100 + (targetGraph.nodes.Count % 8) * 30f, 100 + (targetGraph.nodes.Count % 8) * 30f);
            PersistGraph();
            RebuildGraphView();
        }

        private void PersistGraph()
        {
            if(targetGraph == null) return;
            EditorUtility.SetDirty(targetGraph);
            AssetDatabase.SaveAssetIfDirty(targetGraph);
        }

        private DialogueEdgeData FindEdge(string fromNodeGuid, string toNodeGuid)
        {
            if(targetGraph == null || targetGraph.edges == null) return null;

            foreach(DialogueEdgeData candidate in targetGraph.edges)
            {
                if(candidate == null) continue;
                if(candidate.fromNodeGuid != fromNodeGuid || candidate.toNodeGuid != toNodeGuid) continue;
                return candidate;
            }

            return null;
        }

        /// <summary>
        /// Targeted in-place refresh of the selected node's preview after an inspector
        /// commit that changed data shown on the node (title, message/condition labels,
        /// port labels). Structural changes still go through RebuildGraphView().
        /// </summary>
        private void RefreshSelectedNodePreview()
        {
            if(selectedNodeData == null || string.IsNullOrEmpty(selectedNodeData.guid)) return;
            if(nodeViews.TryGetValue(selectedNodeData.guid, out DialogueNodeView view) && view != null)
            {
                view.RefreshPreview();
            }
        }

        private void RecordChange(string operation)
        {
            if(targetGraph != null) Undo.RecordObject(targetGraph, operation);
        }

        #endregion

        #region Clipboard Operations

        /// <summary>
        /// Copies the selected nodes plus the edges between them into the in-window
        /// clipboard. GUIDs are NOT remapped here - that happens at paste time so repeated
        /// pastes each get fresh identities. No mutation, no Undo.
        /// </summary>
        private void OnCopyRequested()
        {
            if(graphView == null) return;

            List<DialogueNodeView> selectedViews = new List<DialogueNodeView>();
            foreach(ISelectable selectable in graphView.selection)
            {
                if(selectable is DialogueNodeView nodeView) selectedViews.Add(nodeView);
            }
            if(selectedViews.Count == 0) return;

            HashSet<string> selectedGuids = new HashSet<string>();
            NodeClipboard copy = new NodeClipboard();
            foreach(DialogueNodeView view in selectedViews)
            {
                if(view.NodeData == null || string.IsNullOrEmpty(view.NodeGuid)) continue;
                selectedGuids.Add(view.NodeGuid);
                // JsonUtility round-trip deep-clones the node data and editorPosition;
                // the clipboard is scoped to this editor session.
                copy.nodes.Add(JsonUtility.FromJson<DialogueNodeData>(JsonUtility.ToJson(view.NodeData)));
            }

            if(copy.nodes.Count == 0) return;

            // the payload's edges are the graph edges whose BOTH endpoints are selected
            if(targetGraph != null && targetGraph.edges != null)
            {
                foreach(DialogueEdgeData edge in targetGraph.edges)
                {
                    if(edge == null) continue;
                    if(!selectedGuids.Contains(edge.fromNodeGuid) || !selectedGuids.Contains(edge.toNodeGuid)) continue;
                    copy.edges.Add(new DialogueEdgeData
                    {
                        fromNodeGuid = edge.fromNodeGuid,
                        toNodeGuid = edge.toNodeGuid
                    });
                }
            }

            clipboard = copy;
        }

        /// <summary>
        /// Pastes the in-window clipboard: re-clones each stored node with a fresh GUID,
        /// reconnects internal edges via the old-to-new node remap, persists, rebuilds the
        /// view, and selects the new nodes. One Undo step covers the whole paste - the graph
        /// asset is a single object.
        /// </summary>
        private void OnPasteRequested()
        {
            if(targetGraph == null || clipboard == null || clipboard.nodes.Count == 0) return;

            Undo.RecordObject(targetGraph, "Paste dialogue nodes");

            if(targetGraph.nodes == null) targetGraph.nodes = new List<DialogueNodeData>();

            Dictionary<string, string> nodeRemap = new Dictionary<string, string>();
            List<string> pastedGuids = new List<string>();

            foreach(DialogueNodeData clipNode in clipboard.nodes)
            {
                if(clipNode == null || string.IsNullOrEmpty(clipNode.guid)) continue;

                // re-clone per paste so repeated pastes never share instances
                DialogueNodeData fresh = JsonUtility.FromJson<DialogueNodeData>(JsonUtility.ToJson(clipNode));

                string newNodeGuid = Guid.NewGuid().ToString();
                fresh.guid = newNodeGuid;
                nodeRemap[clipNode.guid] = newNodeGuid;

                // offset from the source position so both stay visible and selectable
                fresh.editorPosition += new Vector2(40f, 40f);
                targetGraph.nodes.Add(fresh);
                pastedGuids.Add(newNodeGuid);
            }

            foreach(DialogueEdgeData edge in clipboard.edges)
            {
                if(edge == null) continue;
                if(!nodeRemap.TryGetValue(edge.fromNodeGuid, out string fromGuid)) continue;
                if(!nodeRemap.TryGetValue(edge.toNodeGuid, out string toGuid)) continue;

                // Connect dedupes identical edges and SetDirty's the asset
                targetGraph.Connect(fromGuid, toGuid);
            }

            PersistGraph();
            RebuildGraphView();

            // ClearSelection/AddToSelection raise SelectionChanged ->
            // OnGraphViewSelectionChanged, which sets selectedNodeData to the first pasted
            // node and repaints the inspector - no need to set selectedNodeData manually
            graphView.ClearSelection();
            foreach(string guid in pastedGuids)
            {
                if(nodeViews.TryGetValue(guid, out DialogueNodeView view) && view != null)
                {
                    graphView.AddToSelection(view);
                }
            }
        }

        /// <summary>
        /// Duplicate = copy + paste immediately.
        /// </summary>
        private void OnDuplicateRequested()
        {
            OnCopyRequested();
            OnPasteRequested();
        }

        /// <summary>
        /// Cut = copy into the clipboard, then remove the source nodes (and their touching
        /// edges) in one Undo step. Kept off OnGraphViewChanged's elementsToRemove path -
        /// that route is for user Delete-key removals and schedules per-element delayed
        /// rebuilds.
        /// </summary>
        private void OnCutRequested()
        {
            if(graphView == null) return;

            // capture the guids BEFORE removing - the removal mutates the graph data
            List<string> removedGuids = new List<string>();
            foreach(ISelectable selectable in graphView.selection)
            {
                if(selectable is DialogueNodeView nodeView && !string.IsNullOrEmpty(nodeView.NodeGuid))
                {
                    removedGuids.Add(nodeView.NodeGuid);
                }
            }
            if(removedGuids.Count == 0) return;

            OnCopyRequested();

            // one Undo step for the whole cut
            Undo.RecordObject(targetGraph, "Cut dialogue nodes");
            foreach(string guid in removedGuids)
            {
                // RemoveNode also drops every edge touching the node
                targetGraph.RemoveNode(guid);
            }

            if(selectedNodeData != null && removedGuids.Contains(selectedNodeData.guid)) selectedNodeData = null;

            PersistGraph();
            RebuildGraphView();
            inspectorContainer?.MarkDirtyRepaint();
        }

        #endregion

        #region Inspector

private void DrawInspector()
        {
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Node Inspector", EditorStyles.boldLabel);

            if(targetGraph == null)
            {
                EditorGUILayout.HelpBox("Select a DialogueGraph asset (in the Project window or via the target field).", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            if(selectedNodeData == null)
            {
                EditorGUILayout.HelpBox("Select a node on the canvas to edit its settings.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField("Kind: " + selectedNodeData.kind, EditorStyles.boldLabel);
            EditorGUILayout.Space();

            switch(selectedNodeData.kind)
            {
                case DialogueNodeKind.Line:
                    DrawLineSettings();
                    break;
                case DialogueNodeKind.Entry:
                    EditorGUILayout.HelpBox("Pure start marker - dialogue begins here.", MessageType.Info);
                    break;
                case DialogueNodeKind.End:
                default:
                    EditorGUILayout.HelpBox("Ends the dialogue when playback reaches it.", MessageType.Info);
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

private void DrawLineSettings()
        {
            if(selectedNodeData.line == null) selectedNodeData.line = new DialogueLine();
            DialogueLine line = selectedNodeData.line;

            EditorGUILayout.LabelField(new GUIContent("Line Data", DialogueEditorTooltips.LineData), EditorStyles.boldLabel);

            // GUILayout.TextArea has no GUIContent overload, so the Message tooltip
            // lives on this header; it still describes the TextArea below it.
            EditorGUILayout.LabelField(new GUIContent("Message", DialogueEditorTooltips.LineMessage), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string currentText = line.text ?? string.Empty;
            string newText = EditorGUILayout.TextArea(currentText, GUILayout.Height(90));
            if(EditorGUI.EndChangeCheck())
            {
                RecordChange("Edit line message");
                line.text = newText;
                PersistGraph();
                RefreshSelectedNodePreview();
            }

            EditorGUI.BeginChangeCheck();
            float newDelay = EditorGUILayout.FloatField(new GUIContent("Delay", DialogueEditorTooltips.LineDelay), line.delaySeconds);
            if(EditorGUI.EndChangeCheck())
            {
                RecordChange("Edit line delay");
                line.delaySeconds = newDelay;
                PersistGraph();
            }
        }







        #endregion

        #region Validation

private void ValidateGraph()
        {
            if(targetGraph == null)
            {
                Debug.LogWarning("Dialogue Graph Editor: no graph selected to validate.");
                return;
            }

            int issueCount = 0;

            if(targetGraph.nodes == null || targetGraph.nodes.Count == 0)
            {
                Debug.LogWarning($"Dialogue Graph '{targetGraph.name}' has no nodes.", targetGraph);
                return;
            }

            // Dangling edges can no longer be followed by the runtime.
            if(targetGraph.edges != null)
            {
                foreach(DialogueEdgeData edge in targetGraph.edges)
                {
                    if(edge == null) continue;
                    if(targetGraph.GetNode(edge.fromNodeGuid) == null || targetGraph.GetNode(edge.toNodeGuid) == null)
                    {
                        Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': dangling edge {edge.fromNodeGuid} -> {edge.toNodeGuid}.", targetGraph);
                        issueCount++;
                    }
                }
            }

            // The first Entry is the runtime start; when there is none, playback falls back to the first node.
            DialogueNodeData firstEntry = null;
            int entryCount = 0;
            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null || node.kind != DialogueNodeKind.Entry) continue;
                entryCount++;
                if(firstEntry == null) firstEntry = node;
            }

            DialogueNodeData reachabilityRoot = firstEntry;
            if(entryCount == 0)
            {
                Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': no Entry node - the first node is used as the fallback entry.", targetGraph);
                issueCount++;
                reachabilityRoot = targetGraph.nodes.FirstOrDefault(node => node != null && !string.IsNullOrEmpty(node.guid));
            }
            else if(entryCount > 1)
            {
                Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': {entryCount} Entry nodes found - only the first is used.", targetGraph);
                issueCount++;
            }

            // Reachability follows every edge from the (first) Entry, or the fallback first node.
            HashSet<string> reachable = new HashSet<string>();
            if(reachabilityRoot != null && !string.IsNullOrEmpty(reachabilityRoot.guid))
            {
                Queue<string> frontier = new Queue<string>();
                reachable.Add(reachabilityRoot.guid);
                frontier.Enqueue(reachabilityRoot.guid);

                while(frontier.Count > 0)
                {
                    string current = frontier.Dequeue();
                    if(targetGraph.edges == null) continue;

                    foreach(DialogueEdgeData edge in targetGraph.edges)
                    {
                        if(edge == null || edge.fromNodeGuid != current) continue;
                        if(reachable.Add(edge.toNodeGuid)) frontier.Enqueue(edge.toNodeGuid);
                    }
                }
            }

            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null || string.IsNullOrEmpty(node.guid)) continue;
                if(reachabilityRoot != null && node.guid == reachabilityRoot.guid) continue;
                if(!reachable.Contains(node.guid))
                {
                    string kindName = node.kind == DialogueNodeKind.Line
                        ? "Line"
                        : node.kind == DialogueNodeKind.Entry ? "Entry" : "End";
                    Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': node '{kindName}' ({node.guid}) is unreachable from the entry.", targetGraph);
                    issueCount++;
                }
            }

            // Content checks for the remaining editable node kinds.
            foreach(DialogueNodeData node in targetGraph.nodes)
            {
                if(node == null) continue;

                switch(node.kind)
                {
                    case DialogueNodeKind.Line:
                    {
                        if(node.line == null || string.IsNullOrEmpty(node.line.text))
                        {
                            Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': line node '{node.guid}' has an empty message.", targetGraph);
                            issueCount++;
                        }
                        break;
                    }
                    case DialogueNodeKind.Entry:
                    {
                        bool hasOutgoing = targetGraph.edges != null
                            && targetGraph.edges.Any(edge => edge != null && edge.fromNodeGuid == node.guid);

                        if(!hasOutgoing)
                        {
                            Debug.LogWarning($"Dialogue Graph '{targetGraph.name}': entry '{node.guid}' has no outgoing edge - it will never play content.", targetGraph);
                            issueCount++;
                        }
                        break;
                    }
                    case DialogueNodeKind.End:
                    default:
                        break;
                }
            }

            Debug.Log(issueCount == 0
                ? $"Dialogue Graph '{targetGraph.name}' validated with no issues."
                : $"Dialogue Graph '{targetGraph.name}' validated with {issueCount} issue(s) - see warnings above.");
        }

        #endregion

        /// <summary>
        /// Single reviewable table of inspector hover-tooltip wording. Each entry explains
        /// the runtime effect of the field rather than restating its label.
        /// </summary>
        private static class DialogueEditorTooltips
        {
            public const string LineData = "Data delivered when this line node plays.";
            public const string LineDelay = "Seconds before this line is delivered (relative to the previous line or the trigger). Negative uses the director default.";
            public const string LineMessage = "The line's message text shown to the player.";
        }
    }
}
