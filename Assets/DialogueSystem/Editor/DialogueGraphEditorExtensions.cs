using System;
using System.Collections.Generic;
using DialogueSystem;
using UnityEditor;
using UnityEngine;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// Editor-only mutation helpers for <see cref="DialogueGraph"/> assets. The runtime
    /// assembly carries no UnityEditor code, so all asset mutation and dirtying lives
    /// here and is shared by the graph editor window, its clipboard operations, and the
    /// canvas rebuild paths.
    /// </summary>
    public static class DialogueGraphEditorExtensions
    {
        /// <summary>
        /// Appends a supported node with a fresh GUID, marks the asset dirty, and returns
        /// the new node.
        /// </summary>
        public static DialogueNodeData AddNode(this DialogueGraph graph, DialogueNodeKind kind)
        {
            if(graph == null) return null;
            if(graph.nodes == null) graph.nodes = new List<DialogueNodeData>();

            DialogueNodeData node = new DialogueNodeData
            {
                guid = Guid.NewGuid().ToString(),
                kind = kind
            };

            switch(kind)
            {
                case DialogueNodeKind.Line:
                    if(node.line == null) node.line = new DialogueLine();
                    break;
                case DialogueNodeKind.Entry:
                case DialogueNodeKind.End:
                    break;
                default:
                    node.kind = DialogueNodeKind.End;
                    break;
            }

            graph.nodes.Add(node);
            EditorUtility.SetDirty(graph);
            return node;
        }

        /// <summary>
        /// Removes the node with <paramref name="guid"/> and every edge that touches it
        /// as either the source or the target, then marks the asset dirty.
        /// </summary>
        public static void RemoveNode(this DialogueGraph graph, string guid)
        {
            if(graph == null || string.IsNullOrEmpty(guid)) return;

            if(graph.nodes != null)
            {
                graph.nodes.RemoveAll(node => node != null && node.guid == guid);
            }

            if(graph.edges != null)
            {
                graph.edges.RemoveAll(edge =>
                    edge != null && (edge.fromNodeGuid == guid || edge.toNodeGuid == guid));
            }

            EditorUtility.SetDirty(graph);
        }

        /// <summary>
        /// Adds an edge from <paramref name="fromNodeGuid"/> to <paramref name="toNodeGuid"/>.
        /// Identical edges are deduped, and the asset is marked dirty when an edge is added.
        /// </summary>
        public static void Connect(this DialogueGraph graph, string fromNodeGuid, string toNodeGuid)
        {
            if(graph == null) return;
            if(string.IsNullOrEmpty(fromNodeGuid) || string.IsNullOrEmpty(toNodeGuid)) return;
            if(graph.edges == null) graph.edges = new List<DialogueEdgeData>();

            foreach(DialogueEdgeData edge in graph.edges)
            {
                if(edge == null) continue;
                if(edge.fromNodeGuid != fromNodeGuid || edge.toNodeGuid != toNodeGuid) continue;
                return;
            }

            graph.edges.Add(new DialogueEdgeData
            {
                fromNodeGuid = fromNodeGuid,
                toNodeGuid = toNodeGuid
            });

            EditorUtility.SetDirty(graph);
        }

        /// <summary>
        /// Removes the given edge and marks the asset dirty.
        /// </summary>
        public static void Disconnect(this DialogueGraph graph, DialogueEdgeData edge)
        {
            if(graph == null || edge == null) return;
            if(graph.edges == null) return;

            graph.edges.Remove(edge);
            EditorUtility.SetDirty(graph);
        }
    }
}
