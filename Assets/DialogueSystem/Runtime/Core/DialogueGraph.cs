using System.Collections.Generic;
using UnityEngine;

namespace DialogueSystem
{
    /// <summary>
    /// Serialized dialogue asset: a flat node list plus a flat edge list. Lookup helpers are
    /// null-safe. Mutation lives in the editor assembly (DialogueGraphEditorExtensions).
    /// </summary>
    [CreateAssetMenu(fileName = "DialogueGraph", menuName = "Dialogue System/Dialogue Graph")]
    public class DialogueGraph : ScriptableObject
    {
        public List<DialogueNodeData> nodes = new List<DialogueNodeData>();
        public List<DialogueEdgeData> edges = new List<DialogueEdgeData>();

        public DialogueNodeData GetNode(string guid)
        {
            if(nodes == null || string.IsNullOrEmpty(guid)) return null;

            foreach(DialogueNodeData node in nodes)
            {
                if(node != null && node.guid == guid) return node;
            }

            return null;
        }

        /// <summary>First Entry node; if none exists, the first non-null node.</summary>
        public DialogueNodeData GetEntryNode()
        {
            if(nodes == null) return null;

            DialogueNodeData fallback = null;

            foreach(DialogueNodeData node in nodes)
            {
                if(node == null) continue;
                if(node.kind == DialogueNodeKind.Entry) return node;
                if(fallback == null) fallback = node;
            }

            return fallback;
        }

        /// <summary>Target of the first edge leaving the node's single (non-option) output.</summary>
        public DialogueNodeData GetNextNode(string nodeGuid)
        {
            if(edges == null || string.IsNullOrEmpty(nodeGuid)) return null;

            foreach(DialogueEdgeData edge in edges)
            {
                if(edge == null || edge.fromNodeGuid != nodeGuid) continue;
                if(!string.IsNullOrEmpty(edge.fromOptionGuid)) continue;
                return GetNode(edge.toNodeGuid);
            }

            return null;
        }
    }
}
