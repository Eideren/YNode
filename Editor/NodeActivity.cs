using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using YNode.Editor.Internal;
using Object = UnityEngine.Object;

namespace YNode.Editor
{
    public abstract class NodeActivity
    {
        public readonly GraphWindow Window;

        public NodeActivity(GraphWindow window)
        {
            Window = window;
        }

        public abstract void InputPreDraw(Event e);

        public abstract void PreNodeDraw();

        public abstract void PostNodeDraw();

        public abstract void InputPostDraw(Event e);
    }

    public class ConnectPortActivity : NodeActivity
    {
        public readonly Port Port;
        private NodeEditor? _draggedOutputTarget;

        public ConnectPortActivity(Port port, GraphWindow window) : base(window)
        {
            Port = port;
            if (Port.Connection is not null)
            {
                GUI.changed = true;
                Port.Disconnect();
                _draggedOutputTarget = Port.NodeEditor;
            }
        }

        public override void InputPreDraw(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDrag when e.button == 0:
                    // Set target even if we can't connect, so as to prevent auto-conn menu from opening erroneously
                    if (Window.HoveredNode != null && !Port.Connection == Window.HoveredNode && Port.CanConnectTo(Window.HoveredNode.Value.GetType()))
                    {
                        _draggedOutputTarget = Window.HoveredNode;
                    }
                    else
                    {
                        _draggedOutputTarget = null;
                    }

                    Window.Repaint();
                    e.Use();

                    break;
            }
        }

        public override void PreNodeDraw()
        {
            Gradient gradient = Window.GetNoodleGradient(Port, _draggedOutputTarget);
            float thickness = Window.GetNoodleThickness(Port, _draggedOutputTarget);
            NoodlePath path = Window.GetNoodlePath(Port, _draggedOutputTarget);
            NoodleStroke stroke = Port.Stroke;

            if (Port.CachedRect == default)
                return;

            Rect fromRect = Port.CachedRect;
            var gridPoints = new List<Vector2> { fromRect.center };
            if (Port.TryGetReroutePoints(out var reroute))
                gridPoints.AddRange(reroute);

            Vector2 endPoint;
            if (_draggedOutputTarget != null)
                endPoint = Window.GetNodeEndpointPosition(_draggedOutputTarget, Port.Direction);
            else
                endPoint = Window.WindowToGridPosition(Event.current.mousePosition);

            gridPoints.Add(endPoint);

            bool isInput = Port.Direction == IO.Input;
            if (isInput)
                gridPoints.Reverse();

            Window.DrawNoodle(gradient, path, stroke, thickness, gridPoints);
            Window.DrawArrow(Port.Direction, endPoint, gradient.colorKeys[isInput ? 0 : ^1].color);

            GUIStyle portStyle1 = Window.GetPortStyle(Port);
            Color bgcol1 = Color.black;
            Color frcol1 = gradient.colorKeys[0].color;
            bgcol1.a = 0.6f;
            frcol1.a = 0.6f;

            if (Port.TryGetReroutePoints(out reroute))
            {
                // Loop through reroute points again and draw the points
                for (int i1 = 0; i1 < reroute.Count; i1++)
                {
                    // Draw reroute point at position
                    Rect rect1 = ReroutePoint.GetRect(reroute[i1]);
                    rect1 = Window.GridToWindowRect(rect1);

                    Window.DrawPortHandle(rect1, bgcol1, frcol1, portStyle1.normal.background, portStyle1.active.background);
                }
            }
        }

        public override void PostNodeDraw()
        {

        }

        public override void InputPostDraw(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDown when e.button == 0:
                    Port.ClearReroute();
                    Window.Repaint();
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseDown when e.button is 1 or 2:
                    Port.GetReroutePoints().Add(Window.WindowToGridPosition(e.mousePosition));
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseUp when e.button == 0:

                    // If connection is valid, save it
                    if (_draggedOutputTarget != null && Port.CanConnectTo(_draggedOutputTarget.Value.GetType()))
                    {
                        Port.Connect(_draggedOutputTarget);
                    }
                    // Open context menu for auto-connection if there is no target node
                    else if (_draggedOutputTarget == null)
                    {
                        Port.ClearReroute();
                        if (Preferences.GetSettings().DragToCreate)
                        {
                            GenericMenu menu = new GenericMenu();
                            Window.AddContextMenuItems(menu, Port.CanConnectTo, Port.Connect);
                            menu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
                        }
                    }

                    //Release dragged connection
                    _draggedOutputTarget = null;
                    GUI.changed = true;
                    Window.CurrentActivity = null;
                    e.Use();

                    break;
            }
        }
    }

    public class DragNodeActivity : NodeActivity
    {
        public readonly Vector2[] DragOffset;
        public readonly NodeEditor[] Editors;

        public DragNodeActivity(GraphWindow window, Vector2 mousePosition) : base(window)
        {
            var p = window.WindowToGridPosition(mousePosition);

            Editors = Selection.objects.OfType<NodeEditor>().ToArray();
            DragOffset = new Vector2[Editors.Length + window._selectedReroutes.Count];

            for (int i = 0; i < Editors.Length; i++)
            {
                NodeEditor node = Editors[i];
                DragOffset[i] = node.Value.Position - p;
            }

            for (int i = 0; i < window._selectedReroutes.Count; i++)
            {
                DragOffset[Editors.Length + i] = window._selectedReroutes[i].GetPoint() - p;
            }
        }

        public override void InputPreDraw(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseUp when e.button == 0:
                    Window.CurrentActivity = null;
                    e.Use();

                    break;

                case EventType.MouseDrag when e.button == 0:

                    // Holding ctrl inverts grid snap
                    bool gridSnap = Preferences.GetSettings().GridSnap;
                    if (e.control)
                        gridSnap = !gridSnap;

                    Vector2 mousePos = Window.WindowToGridPosition(e.mousePosition);
                    // Move selected nodes with offset
                    for (int i = 0; i < Editors.Length; i++)
                    {
                        NodeEditor node = Editors[i];
                        Undo.RecordObject(node, "Moved Node");
                        Vector2 initial = node.Value.Position;
                        node.Value.Position = mousePos + DragOffset[i];
                        if (gridSnap)
                        {
                            node.Value.Position = new(
                                (Mathf.Round((node.Value.Position.x + 8) / 16) * 16) - 8,
                                (Mathf.Round((node.Value.Position.y + 8) / 16) * 16) - 8);
                        }

                        // Offset portConnectionPoints instantly if a node is dragged so they aren't delayed by a frame.
                        Vector2 offset = node.Value.Position - initial;
                        if (offset.sqrMagnitude > 0)
                        {
                            foreach (var (_, port) in node.Ports)
                            {
                                Rect rect = port.CachedRect;
                                rect.position += offset;
                                port.CachedRect = rect;
                            }
                        }
                    }

                    // Move selected reroutes with offset
                    for (int i = 0; i < Window._selectedReroutes.Count; i++)
                    {
                        Vector2 pos = mousePos + DragOffset[Editors.Length + i];
                        if (gridSnap)
                        {
                            pos.x = Mathf.Round(pos.x / 16) * 16;
                            pos.y = Mathf.Round(pos.y / 16) * 16;
                        }

                        Window._selectedReroutes[i].SetPoint(pos);
                    }

                    Window.Repaint();
                    e.Use();
                    GUI.changed = true;
                    break;
            }
        }

        public override void PreNodeDraw()
        {
        }

        public override void PostNodeDraw()
        {
        }

        public override void InputPostDraw(Event e)
        {
        }
    }

    public class DragGridActivity : NodeActivity
    {
        public DragGridActivity(GraphWindow window) : base(window)
        {
        }

        public override void InputPreDraw(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseUp when e.button is 1 or 2:
                    Window.CurrentActivity = null;
                    e.Use();
                    break;

                case EventType.MouseDrag:
                    Window.PanOffset += e.delta * Window.Zoom;
                    e.Use();
                    break;
            }
        }

        public override void PreNodeDraw()
        {
        }

        public override void PostNodeDraw()
        {
        }

        public override void InputPostDraw(Event e)
        {
        }
    }

    public class BoxSelectActivity : NodeActivity
    {
        public Object[] _initialEditors;
        public ReroutePoint[] _initialReroute;
        public List<Object> _selectedEditors = new();
        public List<ReroutePoint> _selectedReroutes = new();
        public Vector2 _dragBoxStart;

        public BoxSelectActivity(GraphWindow window, Vector2 dragBoxStart) : base(window)
        {
            _initialEditors = Selection.objects.ToArray();
            _initialReroute = window._selectedReroutes.ToArray();
            _selectedEditors.AddRange(_initialEditors);
            _selectedReroutes.AddRange(_initialReroute);
            _dragBoxStart = Window.WindowToGridPosition(dragBoxStart);
        }

        public override void InputPreDraw(Event e)
        {
            switch (e.type)
            {
                case EventType.MouseDrag when e.button == 0:
                    Vector2 boxStartPos = Window.GridToWindowPosition(_dragBoxStart);
                    Vector2 boxSize = e.mousePosition - boxStartPos;
                    if (boxSize.x < 0)
                    {
                        boxStartPos.x += boxSize.x;
                        boxSize.x = Mathf.Abs(boxSize.x);
                    }

                    if (boxSize.y < 0)
                    {
                        boxStartPos.y += boxSize.y;
                        boxSize.y = Mathf.Abs(boxSize.y);
                    }

                    var selectionBox = new Rect(boxStartPos, boxSize);
                    bool append = e.control || e.shift;
                    UpdateSelection(selectionBox, append);
                    Window.Repaint();
                    e.Use();
                    break;

                case EventType.Ignore when e.rawType == EventType.MouseUp: // If release mouse outside window
                case EventType.MouseUp when e.button == 0:
                    Window.CurrentActivity = null;
                    e.Use();
                    break;
            }
        }

        public override void PreNodeDraw()
        {
        }

        public override void PostNodeDraw()
        {
            Vector2 curPos = Window.WindowToGridPosition(Event.current.mousePosition);
            Vector2 size = curPos - _dragBoxStart;
            var rect = new Rect(_dragBoxStart, size);
            rect.position = Window.GridToWindowPosition(rect.position);
            rect.size /= Window.Zoom;
            Handles.DrawSolidRectangleWithOutline(rect, new Color(0, 0, 0, 0.1f), new Color(1, 1, 1, 0.6f));
        }

        public override void InputPostDraw(Event e)
        {

        }

        private void UpdateSelection(Rect windowSpaceSelectionBox, bool append)
        {
            _selectedReroutes.Clear();
            if (append)
                _selectedReroutes.AddRange(_initialReroute);
            _selectedEditors.Clear();
            if (append)
                _selectedEditors.AddRange(_initialEditors);

            var min = Window.WindowToGridPosition(windowSpaceSelectionBox.min);
            var max = Window.WindowToGridPosition(windowSpaceSelectionBox.max);
            var gridSpaceSelection = new Rect(min, max - min);

            foreach ((_, NodeEditor node) in Window.NodesToEditor)
            {
                var nodeRect = new Rect(node.Value.Position, node.CachedSize);
                if (nodeRect.Overlaps(gridSpaceSelection))
                    _selectedEditors.Add(node);

                foreach ((_, Port port) in node.Ports)
                {
                    if (port.Connection is {} target && port.TryGetReroutePoints(out var reroutePoints))
                    {
                        for (int i = 0; i < reroutePoints.Count; i++)
                        {
                            var rerouteRef = new ReroutePoint(port, i);
                            Rect rect = rerouteRef.GetRect();
                            if (rect.Overlaps(gridSpaceSelection))
                                _selectedReroutes.Add(rerouteRef);
                        }
                    }
                }
            }

            Window._selectedReroutes.Clear();
            Window._selectedReroutes.AddRange(_selectedReroutes);
            Selection.objects = _selectedEditors.ToArray();
        }
    }
}
