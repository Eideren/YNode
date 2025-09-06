using System;
using System.Collections.Generic;
using System.Linq;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using YNode.Editor.Internal;
using GenericMenu = YNode.Editor.AdvancedGenericMenu;

namespace YNode.Editor
{
    public partial class GraphWindow
    {
        public static NodeEditor[] CopyBuffer = Array.Empty<NodeEditor>();

        [NonSerialized] private NodeEditor? _hoveredNode = null;
        [NonSerialized] private Port? _hoveredPort = null;
        [NonSerialized] private ReroutePoint? _hoveredReroute = null;

        [NonSerialized] private Vector2 _lastMousePosition;

        [NonSerialized] public List<ReroutePoint> SelectedReroutes = new();
        [NonSerialized] public NodeActivity? CurrentActivity = null;

        /// <summary> Return the Hovered port or null if not exist </summary>
        public Port? HoveredPort => _hoveredPort;

        /// <summary> Return the Hovered node or null if not exist </summary>
        public NodeEditor? HoveredNode => _hoveredNode;

        protected virtual void ControlsPreDraw()
        {
            wantsMouseMove = true;

            Event e = Event.current;
            CurrentActivity?.InputPreDraw(e);
        }

        protected virtual void ControlsPostDraw()
        {
            wantsMouseMove = true;
            if (OdinObjectSelector.IsOpen && CurrentActivity is null)
                CurrentActivity = new OdinSelectorOpen(this);

            Event e = Event.current;
            if (CurrentActivity is not null)
            {
                CurrentActivity.InputPostDraw(e);
                return;
            }

            HandleNodeMapInput();
            switch (e.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    DragAndDrop.visualMode = DragAndDropVisualMode.Generic;
                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        OnDropObjects(DragAndDrop.objectReferences);
                        GUI.changed = true;
                    }

                    break;

                case EventType.MouseMove:
                    //Keyboard commands will not get correct mouse position from Event
                    _lastMousePosition = e.mousePosition;
                    break;

                case EventType.ScrollWheel:
                    float oldZoom = Zoom;
                    if (e.delta.y > 0)
                        Zoom += 0.1f * Zoom;
                    else
                        Zoom -= 0.1f * Zoom;
                    if (Preferences.GetSettings().ZoomToMouse)
                        PanOffset += (1 - oldZoom / Zoom) * (WindowToGridPosition(e.mousePosition) + PanOffset);
                    break;

                case EventType.MouseDrag when CurrentActivity is null && e.button is 0:
                    if (_hoveredNode is not null || _hoveredReroute is not null)
                        CurrentActivity = new DragNodeActivity(this, e.mousePosition);
                    else
                        CurrentActivity = new BoxSelectActivity(this, e.mousePosition);
                    break;

                case EventType.MouseDrag when CurrentActivity is null && e.button is 1 or 2:
                    PanOffset += e.delta * Zoom;
                    CurrentActivity = new DragGridActivity(this);
                    break;

                case EventType.MouseDown when e.button == 0:

                    Repaint();

                    if (_hoveredPort != null)
                    {
                        CurrentActivity = new ConnectPortActivity(_hoveredPort, this);
                    }
                    else if (_hoveredReroute is { } hoveredRerouteValue)
                    {
                        GUI.changed = true;
                        if (SelectedReroutes.Contains(hoveredRerouteValue))
                        {
                            if (e.control || e.shift)
                                SelectedReroutes.Remove(hoveredRerouteValue);
                        }
                        else
                        {
                            if (e.control || e.shift)
                            {
                                SelectedReroutes.Add(hoveredRerouteValue);
                            }
                            else
                            {
                                SelectedReroutes = new List<ReroutePoint> { hoveredRerouteValue };
                                Selection.activeObject = null;
                            }
                        }

                        e.Use();
                    }
                    else if (_hoveredNode != null)
                    {
                        if (Selection.Contains(_hoveredNode))
                        {
                            if (e.control || e.shift)
                                DeselectNode(_hoveredNode);
                        }
                        else
                        {
                            bool add = e.control || e.shift;
                            SelectNode(_hoveredNode, add);
                            if (!add)
                                SelectedReroutes.Clear();
                        }

                        if (e.clickCount == 2)
                        {
                            Vector2 nodeDimension = _hoveredNode.CachedSize / 2;
                            PanOffset = -_hoveredNode.Value.Position - nodeDimension;
                        }

                        e.Use();
                    }
                    // If mousedown on grid background, deselect all
                    else
                    {
                        if (!e.control && !e.shift)
                        {
                            SelectedReroutes.Clear();
                            Selection.activeObject = null;
                        }
                    }

                    break;
                case EventType.MouseUp when e.button == 0:
                    if (_hoveredNode == null)
                    {
                        // If click outside node, release field focus
                        EditorGUI.FocusTextInControl(null);
                        EditorGUIUtility.editingTextField = false;
                    }
                    break;

                case EventType.MouseUp when e.button is 1 or 2 && CurrentActivity is null:
                    if (_hoveredReroute is {} hoveredRerouteValue2)
                    {
                        ShowRerouteContextMenu(hoveredRerouteValue2);
                        e.Use();
                    }
                    else if (_hoveredPort != null)
                    {
                        ShowPortContextMenu(_hoveredPort);
                        e.Use();
                    }
                    else if (_hoveredNode != null && IsHoveringTitle(_hoveredNode))
                    {
                        if (!Selection.Contains(_hoveredNode))
                            SelectNode(_hoveredNode, false);

                        GenericMenu menu = new GenericMenu();
                        _hoveredNode.AddContextMenuItems(menu);
                        menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                        e.Use();
                    }
                    else if (_hoveredNode == null)
                    {
                        GenericMenu menu = new GenericMenu();
                        AddContextMenuItems(menu, null, null);
                        menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                        e.Use();
                    }

                    break;
                case EventType.KeyDown when EditorGUIUtility.editingTextField == false && GUIUtility.keyboardControl == 0:
                    if (e.keyCode == KeyCode.F)
                    {
                        Home();
                        e.Use();
                    }
                    else if (e.keyCode == KeyCode.A)
                    {
                        if (Selection.objects.Any(x => x is NodeEditor n && _nodesToEditor.ContainsKey(n.Value)))
                        {
                            foreach (var (_, node) in _nodesToEditor)
                            {
                                DeselectNode(node);
                            }
                        }
                        else
                        {
                            foreach (var (_, node) in _nodesToEditor)
                            {
                                SelectNode(node, true);
                            }
                        }

                        Repaint();
                        e.Use();
                    }

                    break;
                case EventType.ValidateCommand:
                case EventType.ExecuteCommand:
                    if (e.commandName == "SoftDelete")
                    {
                        if (e.type == EventType.ExecuteCommand)
                        {
                            RemoveSelectedNodes();
                            GUI.changed = true;
                        }

                        e.Use();
                    }
                    else if (Utilities.IsMac() && e.commandName == "Delete")
                    {
                        if (e.type == EventType.ExecuteCommand)
                        {
                            RemoveSelectedNodes();
                            GUI.changed = true;
                        }

                        e.Use();
                    }
                    else if (e.commandName == "Duplicate")
                    {
                        if (e.type == EventType.ExecuteCommand)
                        {
                            DuplicateSelectedNodes();
                            GUI.changed = true;
                        }
                        e.Use();
                    }
                    else if (e.commandName == "Copy")
                    {
                        if (!EditorGUIUtility.editingTextField)
                        {
                            if (e.type == EventType.ExecuteCommand)
                            {
                                CopySelectedNodes();
                                GUI.changed = true;
                            }
                            e.Use();
                        }
                    }
                    else if (e.commandName == "Paste")
                    {
                        if (!EditorGUIUtility.editingTextField)
                        {
                            if (e.type == EventType.ExecuteCommand)
                            {
                                PasteNodes(WindowToGridPosition(_lastMousePosition));
                                GUI.changed = true;
                            }
                            e.Use();
                        }
                    }

                    Repaint();
                    break;
            }
        }

        /// <summary> Puts all selected nodes in focus. If no nodes are present, resets view and zoom to to origin </summary>
        public void Home()
        {
            var nodes = Selection.objects.OfType<NodeEditor>().ToList();
            if (nodes.Count > 0)
            {
                Vector2 minPos = nodes.Select(x => x.Value.Position)
                    .Aggregate((x, y) => new Vector2(Mathf.Min(x.x, y.x), Mathf.Min(x.y, y.y)));
                Vector2 maxPos = nodes
                    .Select(x => x.Value.Position + x.CachedSize)
                    .Aggregate((x, y) => new Vector2(Mathf.Max(x.x, y.x), Mathf.Max(x.y, y.y)));
                PanOffset = -(minPos + (maxPos - minPos) / 2f);
            }
            else
            {
                Zoom = 2;
                PanOffset = Vector2.zero;
            }
        }

        /// <summary> Remove nodes in the graph in Selection.objects</summary>
        public void RemoveSelectedNodes()
        {
            // We need to delete reroutes starting at the highest point index to avoid shifting indices
            SelectedReroutes = SelectedReroutes.OrderByDescending(x => x.PointIndex).ToList();
            for (int i = 0; i < SelectedReroutes.Count; i++)
            {
                SelectedReroutes[i].RemovePoint();
            }

            SelectedReroutes.Clear();
            foreach (var item in Selection.objects.ToArray())
            {
                if (item is NodeEditor node)
                    RemoveNode(node);
            }
        }

        /// <summary> Draw this node on top of other nodes by placing it last in the graph.nodes list </summary>
        public void MoveNodeToTop(NodeEditor nodeEditor)
        {
            var val = nodeEditor.Value;
            var index = Graph.Nodes.IndexOf(val);
            Graph.Nodes.RemoveAt(index);
            Graph.Nodes.Add(val);
        }

        /// <summary> Duplicate selected nodes and select the duplicates </summary>
        public void DuplicateSelectedNodes()
        {
            // Get selected nodes which are part of this graph
            NodeEditor[] selectedNodes = Selection.objects.OfType<NodeEditor>().Where(x => x.Graph == Graph).ToArray();
            if (selectedNodes.Length == 0) return;
            // Get top left node position
            Vector2 topLeftNode = selectedNodes.Select(x => x.Value.Position)
                .Aggregate((x, y) => new Vector2(Mathf.Min(x.x, y.x), Mathf.Min(x.y, y.y)));
            InsertDuplicateNodes(selectedNodes, topLeftNode + new Vector2(30, 30));
        }

        public void CopySelectedNodes()
        {
            CopyBuffer = Selection.objects.OfType<NodeEditor>().Where(x => x.Graph == Graph).ToArray();
        }

        public void PasteNodes(Vector2 pos)
        {
            InsertDuplicateNodes(CopyBuffer, pos);
        }

        private void InsertDuplicateNodes(NodeEditor[] nodes, Vector2 topLeft)
        {
            if (nodes.Length == 0) return;

            // Get top-left node
            Vector2 topLeftNode = nodes.Select(x => x.Value.Position)
                .Aggregate((x, y) => new Vector2(Mathf.Min(x.x, y.x), Mathf.Min(x.y, y.y)));
            Vector2 offset = topLeft - topLeftNode;

            UnityEngine.Object[] newNodes = new UnityEngine.Object[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                NodeEditor srcNodeEditor = nodes[i];
                if (srcNodeEditor == null) continue;

                // Check if user is allowed to add more of given node type
                Type nodeType = srcNodeEditor.GetType();
                if (Utilities.GetAttrib<DisallowMultipleNodesAttribute>(nodeType, out var disallowAttrib))
                {
                    int typeCount = Graph.Nodes.Count(x => x.GetType() == nodeType);
                    if (typeCount >= disallowAttrib.max) continue;
                }

                NodeEditor newNodeEditor = CopyNode(srcNodeEditor.Value);
                newNodeEditor.Value.Position = srcNodeEditor.Value.Position + offset;
                newNodes[i] = newNodeEditor;
            }

            EditorUtility.SetDirty(Graph);
            // Select the new nodes
            Selection.objects = newNodes;
        }

        private bool IsHoveringTitle(NodeEditor nodeEditor)
        {
            Vector2 mousePos = Event.current.mousePosition;
            //Get node position
            Vector2 nodePos = GridToWindowPosition(nodeEditor.Value.Position);
            float width = nodeEditor.CachedSize.x == 0 ? 200 : nodeEditor.CachedSize.x;
            var windowRect = new Rect(nodePos, new Vector2(width / Zoom, NodeEditor.TitleHeight / Zoom));
            return windowRect.Contains(mousePos);
        }

        public Vector2 GetNodeEndpointPosition(Vector2 startPos, NodeEditor nodeEditor, IO direction)
        {
            Vector2 pos;
            if (_stickyEditors.Contains(nodeEditor))
            {
                pos = GetStickyGridPosition(nodeEditor);
            }
            else
            {
                float min = nodeEditor.Value.Position.y;
                float max = min+nodeEditor.CachedSize.y;
                pos = new Vector2(nodeEditor.Value.Position.x, Mathf.Clamp(startPos.y, min+10, max-10));
            }

            if (direction == IO.Input)
                pos += new Vector2(nodeEditor.GetWidth() + ArrowWidth, 0);
            else
                pos += new Vector2(-ArrowWidth, 0);


            return pos;
        }
    }
}
