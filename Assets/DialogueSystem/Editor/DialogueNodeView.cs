using System.Collections.Generic;
using DialogueSystem;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace DialogueSystem.EditorTools
{
    /// <summary>
    /// GraphView node view for one <see cref="DialogueNodeData"/>. Each view maps back to
    /// a node GUID and renders only the node's runtime data.
    /// </summary>
    public class DialogueNodeView : Node
    {
        public DialogueNodeData NodeData { get; private set; }
        public string NodeGuid => NodeData != null ? NodeData.guid : string.Empty;
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        // Tracks labels so RefreshPreview() can clear them without touching ports.
        private readonly List<Label> previewLabels = new List<Label>();

        public DialogueNodeView(DialogueNodeData nodeData)
        {
            NodeData = nodeData;
            SetPosition(new Rect(nodeData.editorPosition, Vector2.zero));

            // Port.edgeConnector is read-only in Unity 6; InstantiatePort() builds a Port
            // with the default listener, which routes new edges into edgesToCreate. The
            // editor window handles edge creation and persists it through graph.Connect().
            // typeof(object) matches Unity's built-in PortSource<object> adapter, allowing
            // any output to connect to any input. The blank name hides the type label.
            // The input is Multi for fan-in. Outputs are Single because the runtime follows
            // only the first outgoing edge from a node.
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(object));
            InputPort.portName = "";
            inputContainer.Add(InputPort);

            switch(nodeData.kind)
            {
                case DialogueNodeKind.Line:
                case DialogueNodeKind.Entry:
                    OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Single, typeof(object));
                    OutputPort.portName = "";
                    outputContainer.Add(OutputPort);
                    break;
                case DialogueNodeKind.End:
                default:
                    break;
            }

            RefreshPreview();
            RefreshExpandedState();
            RefreshPorts();
        }

        /// <summary>
        /// Rebuilds the title and data preview labels. Ports are created once in the
        /// constructor, so refreshing the preview does not disturb existing connections.
        /// </summary>
        public void RefreshPreview()
        {
            foreach(Label label in previewLabels) label.RemoveFromHierarchy();
            previewLabels.Clear();

            switch(NodeData.kind)
            {
                case DialogueNodeKind.Line:
                    title = "Line";
                    AddMessagePreview(NodeData.line != null ? NodeData.line.text : string.Empty);
                    break;
                case DialogueNodeKind.Entry:
                    title = "Entry";
                    AddDescriptionPreview("Pure start marker - dialogue begins here.");
                    break;
                case DialogueNodeKind.End:
                default:
                    title = "End";
                    AddDescriptionPreview("Ends the dialogue when playback reaches it.");
                    break;
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        private void AddMessagePreview(string message)
        {
            string preview = string.IsNullOrEmpty(message) ? "(empty message)" : message;
            if(preview.Length > 60) preview = preview.Substring(0, 60) + "...";
            Label label = new Label(preview);
            previewLabels.Add(label);
            mainContainer.Add(label);
        }

        private void AddDescriptionPreview(string description)
        {
            Label label = new Label(description);
            previewLabels.Add(label);
            mainContainer.Add(label);
        }
    }
}
