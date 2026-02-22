using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using YNode.Editor.Internal;
using Object = UnityEngine.Object;
using GenericMenu = YNode.Editor.AdvancedGenericMenu;

namespace YNode.Editor
{
    /// <summary> Contains GUI methods </summary>
    public partial class GraphWindow
    {
        private const float ArrowWidth = 16;
        private static readonly Vector3[] s_polyLineTempArray = new Vector3[2];

        private HashSet<NodeEditor> _culledEditors = new();
        private HashSet<NodeEditor> _stickyEditors = new();
        private bool _firstRun = true;
        [NonSerialized] private string? _title, _titleModified;

        /// <summary> 19 if docked, 21 if not </summary>
        private int TopPadding => IsDocked() ? 19 : 21;
        private DateTime? _lastChange;

        /// <summary> Executed after all other window GUI. Useful if Zoom is ruining your day. Automatically resets after being run.</summary>
        public event Action? OnLateGUI;

        public HashSet<NodeEditor> LossyConnectedEditors = new();

        protected virtual void OnGUI()
        {
            if (_ranLoad == false)
            {
                _firstRun = true;
                Load();
            }

            Current = this;
            if (Graph == null)
                return;

            _title ??= Graph.name;
            _titleModified ??= $"{_title}*";
            titleContent.text = EditorUtility.IsDirty(Graph) ? _titleModified : _title;

            Matrix4x4 m = GUI.matrix;

            EditorGUI.BeginChangeCheck();

            ControlsPreDraw();
            DrawGrid(position, Zoom, PanOffset);
            DrawConnections();
            CurrentActivity?.PreNodeDraw();
            DrawNodes();
            CurrentActivity?.PostNodeDraw();
            DrawNodeMap();
            DrawTooltip();
            OnGUIOverlay();
            ControlsPostDraw();

            // Run and reset onLateGUI
            if (OnLateGUI != null)
            {
                OnLateGUI();
                OnLateGUI = null;
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(Graph);
                if (Preferences.GetSettings().AutoSave)
                {
                    if (_lastChange == null)
                        EditorApplication.update += AutoSave;
                    _lastChange = DateTime.Now;
                }
            }

            GUI.matrix = m;
            if (Event.current.type == EventType.Repaint)
                _firstRun = false;
        }

        private void AutoSave()
        {
            if (_lastChange is null)
                throw new InvalidOperationException();

            if ((DateTime.Now - _lastChange.Value).Seconds > 3)
            {
                Save();
                EditorApplication.update -= AutoSave;
                _lastChange = null;
            }
        }

        protected abstract bool StickyEditorEnabled { get; }

        protected virtual void OnGUIOverlay() { }

        private static void BeginZoomed(Rect rect, float zoom, float topPadding)
        {
            GUI.EndClip();

            GUIUtility.ScaleAroundPivot(Vector2.one / zoom, rect.size * 0.5f);
            GUI.BeginClip(new Rect(-((rect.width * zoom) - rect.width) * 0.5f,
                -(((rect.height * zoom) - rect.height) * 0.5f) + (topPadding * zoom),
                rect.width * zoom,
                rect.height * zoom));
        }

        private static void EndZoomed(Rect rect, float zoom, float topPadding)
        {
            GUIUtility.ScaleAroundPivot(Vector2.one * zoom, rect.size * 0.5f);
            Vector3 offset = new Vector3(
                (((rect.width * zoom) - rect.width) * 0.5f),
                (((rect.height * zoom) - rect.height) * 0.5f) + (-topPadding * zoom) + topPadding,
                0);
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one);
        }

        protected virtual void DrawGrid(Rect rect, float zoom, Vector2 panOffset)
        {
            rect.position = Vector2.zero;

            Vector2 center = rect.size / 2f;
            Texture2D gridTex = GetGridTexture();
            Texture2D crossTex = GetSecondaryGridTexture();

            // Offset from origin in tile units
            float xOffset = -(center.x * zoom + panOffset.x) / gridTex.width;
            float yOffset = ((center.y - rect.size.y) * zoom + panOffset.y) / gridTex.height;

            Vector2 tileOffset = new Vector2(xOffset, yOffset);

            // Amount of tiles
            float tileAmountX = Mathf.Round(rect.size.x * zoom) / gridTex.width;
            float tileAmountY = Mathf.Round(rect.size.y * zoom) / gridTex.height;

            Vector2 tileAmount = new Vector2(tileAmountX, tileAmountY);

            // Draw tiled background
            GUI.DrawTextureWithTexCoords(rect, gridTex, new Rect(tileOffset, tileAmount));
            GUI.DrawTextureWithTexCoords(rect, crossTex, new Rect(tileOffset + new Vector2(0.5f, 0.5f), tileAmount));
        }

        /// <summary> Show right-click context menu for hovered reroute </summary>
        private void ShowRerouteContextMenu(ReroutePoint reroute)
        {
            GenericMenu contextMenu = new GenericMenu();
            contextMenu.AddItem(new GUIContent("Remove"), false, () => reroute.RemovePoint());
            contextMenu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
        }

        /// <summary> Show right-click context menu for hovered port </summary>
        private void ShowPortContextMenu(Port hoveredPort)
        {
            GenericMenu contextMenu = new GenericMenu();
            contextMenu.AddItem(new GUIContent("Clear Connections"), false, () => hoveredPort.Disconnect(true));
            //Get compatible nodes with this port
            if (Preferences.GetSettings().CreateFilter)
            {
                contextMenu.AddSeparator("");
                AddContextMenuItems(contextMenu, hoveredPort.CanConnectTo, x => hoveredPort.Connect(x, true));
            }

            contextMenu.DropDown(new Rect(Event.current.mousePosition, Vector2.zero));
        }

        private static Vector2 CalculateBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1 - t;
            float tt = t * t, uu = u * u;
            float uuu = uu * u, ttt = tt * t;
            return new Vector2(
                (uuu * p0.x) + (3 * uu * t * p1.x) + (3 * u * tt * p2.x) + (ttt * p3.x),
                (uuu * p0.y) + (3 * uu * t * p1.y) + (3 * u * tt * p2.y) + (ttt * p3.y)
            );
        }

        /// <summary> Draws a line segment without allocating temporary arrays </summary>
        private static void DrawAAPolyLineNonAlloc(float thickness, Vector2 p0, Vector2 p1)
        {
            s_polyLineTempArray[0].x = p0.x;
            s_polyLineTempArray[0].y = p0.y;
            s_polyLineTempArray[1].x = p1.x;
            s_polyLineTempArray[1].y = p1.y;
            Handles.DrawAAPolyLine(thickness, s_polyLineTempArray);
        }

        /// <summary> Draws a line segment with shadows without allocating temporary arrays </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawAAPolyLineWithShadowNonAlloc(float thickness, Vector2 p0, Vector2 p1)
        {
            s_polyLineTempArray[0].x = p0.x;
            s_polyLineTempArray[0].y = p0.y;
            s_polyLineTempArray[1].x = p1.x;
            s_polyLineTempArray[1].y = p1.y;
            var previousColor = Handles.color;
            Handles.color = Color.black;
            Handles.DrawAAPolyLine(thickness*1.5f, s_polyLineTempArray);
            Handles.color = previousColor;
            Handles.DrawAAPolyLine(thickness, s_polyLineTempArray);
        }

        /// <summary> Draw a bezier from output to input in grid coordinates </summary>
        public void DrawNoodle((Color a, Color b) gradient, NoodlePath path, NoodleStroke stroke, float thickness, List<Vector2> gridPoints)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            // convert grid points to window points
            for (int i = 0; i < gridPoints.Count; ++i)
                gridPoints[i] = GridToWindowPosition(gridPoints[i]);

            Color originalHandlesColor = Handles.color;
            Handles.color = gradient.a;
            int length = gridPoints.Count;
            switch (path)
            {
                case NoodlePath.Curvy:
                    Vector2 outputTangent = Vector2.right;
                    for (int i = 0; i < length - 1; i++)
                    {
                        Vector2 inputTangent;
                        // Cached most variables that repeat themselves here to avoid so many indexer calls :p
                        Vector2 pointA = gridPoints[i];
                        Vector2 pointB = gridPoints[i + 1];
                        float distAb = Vector2.Distance(pointA, pointB);
                        if (i == 0) outputTangent = Zoom * distAb * 0.01f * Vector2.right;
                        if (i < length - 2)
                        {
                            Vector2 pointC = gridPoints[i + 2];
                            Vector2 ab = (pointB - pointA).normalized;
                            Vector2 cb = (pointB - pointC).normalized;
                            Vector2 ac = (pointC - pointA).normalized;
                            Vector2 p = (ab + cb) * 0.5f;
                            float tangentLength = (distAb + Vector2.Distance(pointB, pointC)) * 0.005f * Zoom;
                            float side = ((ac.x * (pointB.y - pointA.y)) - (ac.y * (pointB.x - pointA.x)));

                            p = tangentLength * Mathf.Sign(side) * new Vector2(-p.y, p.x);
                            inputTangent = p;
                        }
                        else
                        {
                            inputTangent = Zoom * distAb * 0.01f * Vector2.left;
                        }

                        // Calculates the tangents for the bezier's curves.
                        float zoomCoef = 50 / Zoom;
                        Vector2 tangentA = pointA + outputTangent * zoomCoef;
                        Vector2 tangentB = pointB + inputTangent * zoomCoef;
                        // Hover effect.
                        int division = Mathf.RoundToInt(.2f * distAb) + 3;

                        var arrV2 = ArrayPool<Vector3>.Shared.Rent(division+1);
                        var arrCol = ArrayPool<Color>.Shared.Rent(division+1);

                        arrV2[0] = pointA;
                        arrCol.AsSpan().Fill(gradient.a);
                        for (int j = 1, draw = 0; j <= division; ++j)
                        {
                            float alpha = 1;
                            if (stroke == NoodleStroke.Dashed)
                            {
                                draw++;
                                if (draw >= 2) draw = -2;
                                if (draw == 0)
                                    alpha = 0;
                            }

                            arrCol[j] = Color.Lerp(gradient.a, gradient.b, (float)i / length + (j + 1f) / division / length);
                            arrCol[j].a *= alpha;
                            Vector2 bezierNext = CalculateBezierPoint(pointA, tangentA, tangentB, pointB, j / (float)division);
                            arrV2[j] = bezierNext;
                        }
                        arrV2.AsSpan()[(division+1)..].Fill(arrV2[division]); // fill rest of the array with same point
                        Handles.DrawAAPolyLine(thickness, arrCol, arrV2);

                        ArrayPool<Color>.Shared.Return(arrCol);
                        ArrayPool<Vector3>.Shared.Return(arrV2);

                        outputTangent = -inputTangent;
                    }

                    break;
                case NoodlePath.Straight:
                    for (int i = 0; i < length - 1; i++)
                    {
                        Vector2 pointA = gridPoints[i];
                        Vector2 pointB = gridPoints[i + 1];
                        // Draws the line with the coloring.
                        Vector2 prevPoint = pointA;
                        // Approximately one segment per 5 pixels
                        int segments = (int)Vector2.Distance(pointA, pointB) / 5;
                        segments = Math.Max(segments, 1);

                        int draw = 0;
                        for (int j = 0; j <= segments; j++)
                        {
                            draw++;
                            float t = j / (float)segments;
                            Vector2 lerp = Vector2.Lerp(pointA, pointB, t);
                            if (draw > 0)
                            {
                                if (i == length - 2) Handles.color = Color.Lerp(gradient.a, gradient.b, t);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, prevPoint, lerp);
                            }

                            prevPoint = lerp;
                            if (stroke == NoodleStroke.Dashed && draw >= 2) draw = -2;
                        }
                    }

                    break;
                case NoodlePath.Angled:
                    for (int i = 0; i < length - 1; i++)
                    {
                        if (i == length - 1) continue; // Skip last index
                        if (gridPoints[i].x <= gridPoints[i + 1].x - (50 / Zoom))
                        {
                            float midpoint = (gridPoints[i].x + gridPoints[i + 1].x) * 0.5f;
                            Vector2 start1 = gridPoints[i];
                            Vector2 end1 = gridPoints[i + 1];
                            start1.x = midpoint;
                            end1.x = midpoint;
                            if (i == length - 2)
                            {
                                DrawAAPolyLineWithShadowNonAlloc(thickness, gridPoints[i], start1);
                                Handles.color = Color.Lerp(gradient.a, gradient.b, 0.5f);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start1, end1);
                                Handles.color = gradient.b;
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end1, gridPoints[i + 1]);
                            }
                            else
                            {
                                DrawAAPolyLineWithShadowNonAlloc(thickness, gridPoints[i], start1);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start1, end1);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end1, gridPoints[i + 1]);
                            }
                        }
                        else
                        {
                            float midpoint = (gridPoints[i].y + gridPoints[i + 1].y) * 0.5f;
                            Vector2 start1 = gridPoints[i];
                            Vector2 end1 = gridPoints[i + 1];
                            start1.x += 25 / Zoom;
                            end1.x -= 25 / Zoom;
                            Vector2 start2 = start1;
                            Vector2 end2 = end1;
                            start2.y = midpoint;
                            end2.y = midpoint;
                            if (i == length - 2)
                            {
                                DrawAAPolyLineWithShadowNonAlloc(thickness, gridPoints[i], start1);
                                Handles.color = Color.Lerp(gradient.a, gradient.b, 0.25f);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start1, start2);
                                Handles.color = Color.Lerp(gradient.a, gradient.b, 0.5f);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start2, end2);
                                Handles.color = Color.Lerp(gradient.a, gradient.b, 0.75f);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end2, end1);
                                Handles.color = gradient.b;
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end1, gridPoints[i + 1]);
                            }
                            else
                            {
                                DrawAAPolyLineWithShadowNonAlloc(thickness, gridPoints[i], start1);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start1, start2);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, start2, end2);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end2, end1);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, end1, gridPoints[i + 1]);
                            }
                        }
                    }

                    break;
                case NoodlePath.ShaderLab:
                    Vector2 start = gridPoints[0];
                    Vector2 end = gridPoints[length - 1];
                    //Modify first and last point in array so we can loop trough them nicely.
                    gridPoints[0] = gridPoints[0] + Vector2.right * (20 / Zoom);
                    gridPoints[length - 1] = gridPoints[length - 1] + Vector2.left * (20 / Zoom);
                    //Draw first vertical lines going out from nodes
                    Handles.color = gradient.a;
                    DrawAAPolyLineWithShadowNonAlloc(thickness, start, gridPoints[0]);
                    Handles.color = gradient.b;
                    DrawAAPolyLineWithShadowNonAlloc(thickness, end, gridPoints[length - 1]);
                    for (int i = 0; i < length - 1; i++)
                    {
                        Vector2 pointA = gridPoints[i];
                        Vector2 pointB = gridPoints[i + 1];
                        // Draws the line with the coloring.
                        Vector2 prevPoint = pointA;
                        // Approximately one segment per 5 pixels
                        int segments = (int)Vector2.Distance(pointA, pointB) / 5;
                        segments = Math.Max(segments, 1);

                        int draw = 0;
                        for (int j = 0; j <= segments; j++)
                        {
                            draw++;
                            float t = j / (float)segments;
                            Vector2 lerp = Vector2.Lerp(pointA, pointB, t);
                            if (draw > 0)
                            {
                                if (i == length - 2) Handles.color = Color.Lerp(gradient.a, gradient.b, t);
                                DrawAAPolyLineWithShadowNonAlloc(thickness, prevPoint, lerp);
                            }

                            prevPoint = lerp;
                            if (stroke == NoodleStroke.Dashed && draw >= 2) draw = -2;
                        }
                    }

                    gridPoints[0] = start;
                    gridPoints[length - 1] = end;
                    break;
            }

            Handles.color = originalHandlesColor;
        }

        private static List<Vector2> _gridPointsCache = new();

        /// <summary> Draws all connections </summary>
        public void DrawConnections()
        {
            Vector2 mousePos = Event.current.mousePosition;
            _hoveredReroute = null;

            if (Event.current.type is EventType.Layout or EventType.Repaint == false)
                return;

            LossyConnectedEditors.Clear();

            if (Event.current.type == EventType.Layout)
                _hoveredPort = null;

            Color col = GUI.color;
            foreach ((_, NodeEditor node) in _nodesToEditor)
            {
                // Draw full connections and output > reroute
                foreach ((_, Port port) in node.ActivePorts)
                {
                    //Needs cleanup. Null checks are ugly
                    Rect fromRect = port.CachedRect;
                    if (fromRect == default)
                        continue;

                    Color portColor = GetPortColor(port);
                    GetPortStyle(port, out var portActive, out var portNormal, out _);

                    var portRect = fromRect;
                    Color backgroundColor = GetPortBackgroundColor(port);

                    var portRectInWindowSpace = GridToWindowRect(portRect);

                    if (portRectInWindowSpace.Contains(mousePos))
                        _hoveredPort = port;

                    if (port.ConnectedEditor is {} target)
                    {
                        LossyConnectedEditors.Add(target);
                        var endPosition = GetNodeEndpointPosition(fromRect.center, target, port.Direction);
                        var toRect = new Rect(endPosition, default);
                        if (port.Direction == IO.Input)
                            (fromRect, toRect) = (toRect, fromRect);

                        port.TryGetReroutePoints(out var reroutePoints);

                        _gridPointsCache.Clear();
                        _gridPointsCache.Add(fromRect.center);
                        if (reroutePoints != null)
                            _gridPointsCache.AddRange(reroutePoints);
                        _gridPointsCache.Add(toRect.center);

                        Vector2 min = toRect.center;
                        Vector2 max = toRect.center;
                        foreach (var point in _gridPointsCache)
                        {
                            min = Vector2.Min(min, point);
                            max = Vector2.Max(max, point);
                        }
                        Rect boundingBox = default;
                        boundingBox.min = min;
                        boundingBox.max = max;

                        if (ShouldGridRectBeCulled(boundingBox) == false)
                        {
                            NoodleStroke noodleStroke = port.Stroke;
                            float noodleThickness = GetNoodleThickness(port, target);
                            NoodlePath noodlePath = GetNoodlePath(port, target);
                            var noodleGradient = GetNoodleGradient(port, target);

                            var arrowRect = DrawArrow(port.Direction, endPosition, port.Direction == IO.Input ? noodleGradient.a : noodleGradient.b);
                            if (arrowRect.Contains(mousePos))
                                _hoveredPort = port;
                            DrawNoodle(noodleGradient, noodlePath, noodleStroke, noodleThickness, _gridPointsCache);

                            if (reroutePoints != null)
                            {
                                // Loop through reroute points again and draw the points
                                for (int i = 0; i < reroutePoints.Count; i++)
                                {
                                    ReroutePoint rerouteRef = new ReroutePoint(port, i);
                                    // Draw reroute point at position
                                    Rect rect = rerouteRef.GetRect();
                                    rect = GridToWindowRect(rect);

                                    // Draw selected reroute points with an outline
                                    if (SelectedReroutes.Contains(rerouteRef))
                                    {
                                        GUI.color = Preferences.GetSettings().HighlightColor;
                                        GUI.DrawTexture(rect, portNormal);
                                    }

                                    GUI.color = portColor;
                                    GUI.DrawTexture(rect, portActive);
                                    if (rect.Contains(mousePos))
                                        _hoveredReroute = rerouteRef;
                                }
                            }
                        }
                    }

                    if (ShouldWindowRectBeCulled(portRectInWindowSpace) == false)
                        DrawPortHandle(portRectInWindowSpace, backgroundColor, portColor, portNormal, portActive);
                }
            }

            GUI.color = col;
        }

        public Rect DrawArrow(IO io, Vector2 point, Color color)
        {
            bool isInput = io == IO.Input;

            Rect rect = default;
            rect.size = new Vector2(ArrowWidth, ArrowWidth) * 2f;
            rect.center = point + Vector2.right * ArrowWidth * 0.5f * (isInput ? -1 : 1) - Vector2.up;

            rect = GridToWindowRect(rect);
            if (ShouldWindowRectBeCulled(rect))
                return rect;

            Texture icon = isInput ? EditorIcons.TriangleLeft.Active : EditorIcons.TriangleRight.Active;
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, icon);
            GUI.color = previousColor;

            return rect;
        }

        private void DrawNodes()
        {
            Event e = Event.current;
            if (CurrentActivity is not null && e.type is not EventType.Layout and not EventType.Repaint)
                return; // Do not pass input Events to nodes when doing an activity, improves performance of activities

            BeginZoomed(position, Zoom, TopPadding);

            Vector2 mousePos = e.mousePosition;

            //Save guiColor so we can revert it
            Color guiColor = GUI.color;

            if (e.type == EventType.Layout)
            {
                _culledEditors.Clear();

                _stickyEditors.Clear();

                if (StickyEditorEnabled)
                {
                    foreach (Object o in Selection.objects)
                    {
                        if (o is not NodeEditor editor)
                            continue;

                        _stickyEditors.Add(editor);
                        foreach (var kvp in editor.ActivePorts)
                        {
                            var connection = kvp.Value.ConnectedEditor;
                            if (connection is not null)
                                _stickyEditors.Add(connection);
                        }

                        foreach (var (otherNode, otherEditor) in _nodesToEditor)
                        {
                            foreach (var (path, port) in otherEditor.ActivePorts)
                            {
                                if (port.Connected != editor.Value)
                                    continue;

                                _stickyEditors.Add(otherEditor);
                                break;
                            }
                        }
                    }
                }
            }

            foreach (var (value, editor) in _nodesToEditor)
            {
                if (Graph.Nodes.Contains(value))
                    continue;

                foreach (var (node2, editor2) in _nodesToEditor.ToArray())
                {
                    if (Graph.Nodes.Contains(node2) == false)
                        RemoveNode(editor2, false);
                }
                break;
            }

            {
                var arr = ArrayPool<NodeEditor>.Shared.Rent(_nodesToEditor.Values.Count);
                _nodesToEditor.Values.CopyTo(arr, 0); // this collection may be modified while iterated
                var oldHovered = _hoveredNode;
                for (int i = 0, c = _nodesToEditor.Count; i < c; i++)
                {
                    NodeEditor? editor = arr[i];
                    if (_stickyEditors.Contains(editor))
                        continue;

                    DrawNodeEditor(e, editor, false, guiColor, mousePos);
                }

                ArrayPool<NodeEditor>.Shared.Return(arr);

                if (_stickyEditors.Count > 0)
                {
                    var prevColor = GUI.color;
                    GUI.color = new Color(0, 0, 0, 0.5f);
                    GUI.DrawTexture(Rect.MinMaxRect(-10000, -10000, 10000, 10000), Texture2D.whiteTexture);
                    GUI.color = prevColor;

                    foreach (var editor in _stickyEditors)
                        DrawNodeEditor(e, editor, true, guiColor, mousePos);
                }

                if (oldHovered != _hoveredNode)
                    Repaint();
            }

            EndZoomed(position, Zoom, TopPadding);
        }

        private Vector2 GetStickyWindowPosition(NodeEditor nodeEditor)
        {
            Vector2 nodePos = GridToWindowPositionWeird(nodeEditor.Value.Position);

            if (nodePos.x < 0)
                nodePos.x = 0;
            if (nodePos.y < 0)
                nodePos.y = 0;

            if (nodeEditor.CachedSize != default)
            {
                Vector2 size = nodeEditor.CachedSize;
                Vector2 max = nodePos + size;
                if (max.x > position.size.x*_zoom)
                    nodePos.x = position.size.x*_zoom - size.x;
                if (max.y > position.size.y*_zoom)
                    nodePos.y = position.size.y*_zoom - size.y;
            }
            return nodePos;
        }

        private Vector2 GetStickyGridPosition(NodeEditor nodeEditor)
        {
            return GetStickyWindowPosition(nodeEditor) - (position.size * (0.5f * Zoom) + PanOffset);
        }

        private void DrawNodeEditor(Event e, NodeEditor nodeEditor, bool sticky,
            Color guiColor, Vector2 mousePos)
        {
            // Culling
            if (e.type == EventType.Layout)
            {
                // Cull unselected nodes outside view
                if (!Selection.Contains(nodeEditor) && sticky == false && ShouldBeCulled(nodeEditor))
                {
                    _culledEditors.Add(nodeEditor);
                    return;
                }
            }
            else if (_culledEditors.Contains(nodeEditor))
                return;

            if (e.type == EventType.Repaint)
            {
                foreach (var (_, port) in nodeEditor.ActivePorts)
                    port.CachedRect = default;
            }

            //Get node position
            Vector2 nodePos = GridToWindowPositionWeird(nodeEditor.Value.Position);
            if (sticky)
            {
                if (nodePos.x < 0)
                    nodePos.x = 0;
                if (nodePos.y < 0)
                    nodePos.y = 0;
                if (nodeEditor.CachedSize != default)
                {
                    Vector2 size = nodeEditor.CachedSize;
                    Vector2 max = nodePos + size;
                    if (max.x > position.size.x*_zoom)
                        nodePos.x = position.size.x*_zoom - size.x;
                    if (max.y > position.size.y*_zoom)
                        nodePos.y = position.size.y*_zoom - size.y;
                }
            }

            var previousHover = _hoveredNode;
            {
                var nodeRect = new Rect(nodePos, nodeEditor.CachedSize);
                if (nodeRect.Contains(mousePos) && nodeEditor.HitTest(nodeRect, mousePos))
                    _hoveredNode = nodeEditor;
                else if (_hoveredNode == nodeEditor)
                    _hoveredNode = null;
            }

            GUILayout.BeginArea(new Rect(nodePos, new Vector2(nodeEditor.GetWidth(), 4000)));

            bool highlighted = Selection.objects.Contains(nodeEditor);
            var draggedPort = (this.CurrentActivity as ConnectPortActivity)?.Port;
            highlighted |= draggedPort?.CanConnectTo(nodeEditor.Value.GetType()) == true;

            GUI.color = nodeEditor.GetTint();
            GUILayout.BeginVertical(nodeEditor.GetBodyStyle());

            GUI.color = highlighted ? Preferences.GetSettings().HighlightColor : default;
            GUILayout.BeginVertical(nodeEditor.GetBodyHighlightStyle());

            GUI.color = guiColor;

            try
            {
                InNodeEditor = nodeEditor;
                nodeEditor.ActivePorts.Clear();
                nodeEditor.OnHeaderGUI();
                nodeEditor.OnBodyGUI();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                InNodeEditor = null;
            }

            if (EditorUtility.IsDirty(nodeEditor))
            {
                EditorUtility.ClearDirty(nodeEditor);
                EditorUtility.SetDirty(Graph);
            }

            GUILayout.EndVertical();
            GUILayout.EndVertical();

            //Cache data about the node for next frame
            if (e.type == EventType.Repaint)
            {
                Vector2 size = GUILayoutUtility.GetLastRect().size;
                nodeEditor.CachedSize = size;
                if (_hoveredNode == nodeEditor) // Correct data if size changed too drastically
                {
                    var nodeRect = new Rect(nodePos, nodeEditor.CachedSize);
                    if ((nodeRect.Contains(mousePos) && nodeEditor.HitTest(nodeRect, mousePos)) == false)
                        _hoveredNode = previousHover == _hoveredNode ? null : previousHover;
                }

                foreach (var (_, port) in nodeEditor.ActivePorts)
                {
                    Vector2 portHandlePos;
                    if (port.Direction == IO.Output)
                        portHandlePos.x = size.x;
                    else
                        portHandlePos.x = 0;
                    portHandlePos.y = port.CachedHeight;
                    if (_stickyEditors.Contains(nodeEditor))
                        portHandlePos += GetStickyGridPosition(nodeEditor);
                    else
                        portHandlePos += nodeEditor.Value.Position;
                    Rect rect = new Rect(portHandlePos.x - 8, portHandlePos.y - 8, 16, 16);
                    port.CachedRect = rect;
                }
            }

            GUILayout.EndArea();
        }

        public bool ShouldBeCulled(NodeEditor nodeEditor)
        {
            if (_firstRun)
                return false;

            Vector2 nodePos = GridToWindowPositionWeird(nodeEditor.Value.Position);
            if (nodePos.x / _zoom > position.width) return true; // Right
            else if (nodePos.y / _zoom > position.height) return true; // Bottom
            else if (nodeEditor.CachedSize != default)
            {
                Vector2 size = nodeEditor.CachedSize;
                Vector2 max = nodePos + size;
                if (max.x < 0 || max.y < 0)
                    return true;
            }

            return false;
        }

        public bool ShouldGridRectBeCulled(Rect rect)
        {
            if (_firstRun)
                return false;

            return ShouldWindowRectBeCulled(GridToWindowRect(rect));
        }

        public bool ShouldWindowRectBeCulled(Rect rect)
        {
            if (_firstRun)
                return false;

            var screenRect = new Rect(default, position.size);
            return rect.Overlaps(screenRect) == false;
        }

        public void DrawTooltip()
        {
            if (!Preferences.GetSettings().PortTooltips)
                return;

            string? tooltip = null;
            if (_hoveredPort != null)
                tooltip = GetPortTooltip(_hoveredPort);
            else if (_hoveredNode != null && _hoveredNode != null && IsHoveringTitle(_hoveredNode))
                tooltip = _hoveredNode.GetHeaderTooltip();

            if (string.IsNullOrEmpty(tooltip))
                return;

            var content = new GUIContent(tooltip);
            var size = Resources.Styles.Tooltip.CalcSize(content);
            size.x += 8;
            var rect = new Rect(Event.current.mousePosition - (size*new Vector2(0.5f, 1f)), size);
            EditorGUI.LabelField(rect, content, Resources.Styles.Tooltip);
            Repaint();
        }

        /// <summary>
        /// Draw the port
        /// </summary>
        /// <param name="rect">position and size</param>
        /// <param name="backgroundColor">color for background texture of the port. Normaly used to Border</param>
        /// <param name="typeColor"></param>
        /// <param name="border">texture for border of the dot port</param>
        /// <param name="dot">texture for the dot port</param>
        public void DrawPortHandle(Rect rect, Color backgroundColor, Color typeColor, Texture2D border, Texture2D dot)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Color col = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(rect, border);
            GUI.color = typeColor;
            GUI.DrawTexture(rect, dot);
            GUI.color = col;
        }

        protected virtual Rect NodeMap => new Rect(position.size - new Vector2(100, 100), new Vector2(100, 100));

        public void HandleNodeMapInput()
        {
            if (NodeMap.size == default)
                return;

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when CurrentActivity is null && NodeMap.Contains(e.mousePosition):
                case EventType.MouseDrag when CurrentActivity is null && NodeMap.Contains(e.mousePosition):
                    e.Use();
                    CurrentActivity = new NodeMapDragActivity(this);
                    break;
            }
        }

        public void DrawNodeMap()
        {
            if (NodeMap.size == default)
                return;

            var backgroundRect = NodeMap;
            float alpha = backgroundRect.Contains(Event.current.mousePosition) ? 1f : 0.25f;

            var previousColor = GUI.color;
            GUI.color = new Color(0.1f,0.1f,0.1f, alpha);
            GUI.DrawTexture(backgroundRect, Texture2D.whiteTexture);

            Vector2 rangeMin = Vector2.positiveInfinity, rangeMax = Vector2.negativeInfinity;
            foreach (var (node, editor) in _nodesToEditor)
            {
                rangeMin = Vector2.Min(node.Position, rangeMin);
                rangeMax = Vector2.Max(node.Position + editor.CachedSize, rangeMax);
            }

            var rangeSize = rangeMax - rangeMin;
            var centeredRect = backgroundRect;
            centeredRect.y += backgroundRect.height * (1f - rangeSize.y / rangeSize.x) * 0.5f;
            rangeSize.y *= rangeSize.x / rangeSize.y; // We don't want to stretch across our entire available space, use aspect ratio
            foreach (var (node, editor) in _nodesToEditor)
            {
                GUI.color = editor.GetTint() * new Color(1,1,1,alpha);
                Rect rectForThisNode = FitInBackground(node.Position, editor.CachedSize, rangeMin, rangeSize, centeredRect);
                GUI.DrawTexture(rectForThisNode, Texture2D.whiteTexture);
            }

            var viewportMin = WindowToGridPosition(default);
            var viewportMax = WindowToGridPosition(position.size);
            var viewportRect = FitInBackground(viewportMin, viewportMax - viewportMin, rangeMin, rangeSize, centeredRect);
            viewportRect.min = Vector2.Max(viewportRect.min, backgroundRect.min); // Make sure it doesn't go outside the window
            viewportRect.max = Vector2.Min(viewportRect.max, backgroundRect.max);
            GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.2f*alpha);
            GUI.DrawTexture(viewportRect, Texture2D.whiteTexture);

            GUI.color = previousColor;
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown when CurrentActivity is NodeMapDragActivity && backgroundRect.Contains(e.mousePosition):
                case EventType.MouseDrag when CurrentActivity is NodeMapDragActivity && backgroundRect.Contains(e.mousePosition):
                    var normalizedMousePosition = e.mousePosition;
                    normalizedMousePosition -= centeredRect.min;
                    normalizedMousePosition /= (centeredRect.max - centeredRect.min);
                    PanOffset = -(rangeMin + rangeSize * normalizedMousePosition);
                    break;
            }

            static Rect FitInBackground(in Vector2 position, in Vector2 size, in Vector2 rangeMin, in Vector2 rangeSize, in Rect backgroundRect)
            {
                var normalizedMin = (position - rangeMin) / rangeSize;
                var normalizedMax = ((position + size) - rangeMin) / rangeSize;

                Rect rectForThisNode = backgroundRect;
                rectForThisNode.min = backgroundRect.min + backgroundRect.size * normalizedMin;
                rectForThisNode.max = backgroundRect.min + backgroundRect.size * normalizedMax;
                return rectForThisNode;
            }
        }
    }
}
