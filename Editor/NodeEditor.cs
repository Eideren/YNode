using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;
using GenericMenu = YNode.Editor.AdvancedGenericMenu;

namespace YNode.Editor
{
    [Serializable]
    public class NodeEditor : ScriptableObject
    {
        public const int TitleHeight = 30;

        private string? _title;
        private Dictionary<string, Port> _portsRecycling = new();
        private Dictionary<string, Port> _ports = new();

        [SkipPolymorphicField, SerializeReference, HideLabel, InlineProperty, ShowInInspector]
        public INodeValue Value = null!;

        /// <summary> Size in grid space </summary>
        [NonSerialized] public Vector2 CachedSize;

        [NonSerialized] public SerializedObject SerializedObject = null!;
        [NonSerialized] public PropertyTree ObjectTree = null!;
        [NonSerialized] public GraphWindow Window = null!;
        public Dictionary<string, List<Vector2>> ReroutePoints = new();

        public NodeGraph Graph => Window.Graph;

        /// <summary> Iterate over all ports on this node. </summary>
        public Dictionary<string, Port> Ports => _ports;

        public bool IsSelected() => Selection.Contains(this);

        /// <summary> Add a dynamic, serialized port to this node. </summary>
        public Port AddPort(string fieldName, Type type, IO direction, GetConnected getConnected,
            CanConnectTo canConnectTo, SetConnection setConnection, NoodleStroke stroke, string? tooltip = null)
        {
            if (HasPort(fieldName))
            {
                Debug.LogWarning($"Port '{fieldName}' already exists in {name}", this);
                return _ports[fieldName];
            }

            if (_portsRecycling.Remove(fieldName, out var port)
                && port.TryReuseFor(fieldName, this, type, direction, getConnected, canConnectTo, setConnection, stroke, tooltip))
            {

            }
            else
            {
                port = new Port(fieldName, this, type, direction, getConnected, canConnectTo, setConnection, stroke, tooltip);
            }

            _ports.Add(fieldName, port);
            return port;
        }


        /// <summary> Remove a dynamic port from the node </summary>
        public void RemovePort(string fieldName, bool disconnect, bool undo)
        {
            Port? dynamicPort = GetPort(fieldName);
            if (dynamicPort == null) throw new ArgumentException($"port {fieldName} doesn't exist");
            RemovePort(dynamicPort, disconnect, undo);
        }

        /// <summary> Remove a dynamic port from the node </summary>
        public void RemovePort(Port port, bool disconnect, bool undo)
        {
            if (disconnect)
                port.Disconnect(undo);
            _ports.Remove(port.FieldName);
            _portsRecycling[port.FieldName] = port;
            port.MarkRecycled();
        }

        /// <summary> Returns port which matches fieldName </summary>
        public Port? GetPort(string fieldName)
        {
            return _ports.GetValueOrDefault(fieldName);
        }

        public bool HasPort(string fieldName)
        {
            return _ports.ContainsKey(fieldName);
        }

        /// <summary> Disconnect everything from this node </summary>
        public void ClearConnections(bool undo)
        {
            foreach ((_, Port port) in _ports)
                port.Disconnect(undo);
        }

        public virtual void PreRemoval() { }

        public virtual void OnHeaderGUI()
        {
            _title ??= ObjectNames.NicifyVariableName(Value.GetType().Name);
            GUILayout.Label(_title, Resources.Styles.NodeHeader, GUILayout.Height(TitleHeight));

            AddCursorRectFromBody(GUILayoutUtility.GetLastRect(), MouseCursor.Pan);
        }

        /// <summary> Draws standard field editors for all public fields </summary>
        public virtual void OnBodyGUI()
        {
            try
            {
                ObjectTree.BeginDraw(true);

                GUIHelper.PushLabelWidth(84);
                ObjectTree.DrawProperties();
                GUIHelper.PopLabelWidth();

                // Call repaint so that the graph window elements respond properly to layout changes coming from Odin
                if (GUIHelper.RepaintRequested)
                {
                    GUIHelper.ClearRepaintRequest();
                    Window.Repaint();
                }
            }
            finally
            {
                ObjectTree.EndDraw();
            }
        }

        public virtual int GetWidth()
        {
            Type type = Value.GetType();
            return type.TryGetAttributeWidth(out var width) ? width : NodeWidthAttribute.Default;
        }

        public virtual bool HitTest(Rect rect, Vector2 mousePosition)
        {
            return rect.Contains(mousePosition);
        }

        /// <summary> Returns color for target node </summary>
        public virtual Color GetTint()
        {
            Type type = Value.GetType();
            return Window.GetTypeColor(type);
        }

        public virtual GUIStyle GetBodyStyle()
        {
            return Resources.Styles.NodeBody;
        }

        public virtual GUIStyle GetBodyHighlightStyle()
        {
            return Resources.Styles.NodeHighlight;
        }

        /// <summary> Override to display custom node header tooltips </summary>
        public virtual string? GetHeaderTooltip()
        {
            return null;
        }

        /// <summary> Add items for the context menu when right-clicking this node. Override to add custom menu items. </summary>
        public virtual void AddContextMenuItems(GenericMenu menu)
        {
            bool canRemove = true;
            // Actions if only one node is selected
            if (Selection.objects.Length == 1 && Selection.activeObject is NodeEditor node)
            {
                menu.AddItem(new GUIContent("Move To Top"), false, () => Window.MoveNodeToTop(node));

                canRemove = Window.CanRemove(node);
            }

            // Add actions to any number of selected nodes
            menu.AddItem(new GUIContent("Copy"), false, Window.CopySelectedNodes);
            menu.AddItem(new GUIContent("Duplicate"), false, Window.DuplicateSelectedNodes);
            menu.AddItem(new GUIContent("Remove"), false, canRemove ? Window.RemoveSelectedNodes : null!);

            // Custom sctions if only one node is selected
            if (Selection.objects.Length == 1 && Selection.activeObject is NodeEditor node2)
            {
                menu.AddCustomContextMenuItems(node2);
            }
        }

        protected void DrawEditableTitle(ref string title)
        {
            var c = new GUIContent(title);
            Resources.Styles.NodeHeader.CalcMinMaxWidth(c, out float minWidth, out float maxWidth);
            minWidth = MathF.Max(10, minWidth);

            var titleRect = GUILayoutUtility.GetRect(minWidth, minWidth, TitleHeight, TitleHeight);
            var center = titleRect.center;
            titleRect.xMin = center.x - minWidth / 2f;
            titleRect.xMax = center.x + minWidth / 2f;

            AddCursorRectFromBody(titleRect, MouseCursor.Text);
            var e = Event.current;
            // Prevent focus on this text unless the user specifically double-clicked on the title of this node
            if (e.type is EventType.MouseDown
                && e.clickCount != 2
                && e.button == 0
                && titleRect.Contains(e.mousePosition)
                && GUIUtility.keyboardControl == 0)
            {
                GUI.Label(titleRect, title, Resources.Styles.NodeHeader);
            }
            else
                title = EditorGUI.TextField(titleRect, title, Resources.Styles.NodeHeader);
        }

        protected void AddCursorRectFromBody(Rect r, MouseCursor m, int controlID = 0)
        {
            r.position += Value.Position;
            r = Window.GridToWindowRect(r);
            r.y += 18;
            s_internalAddCursorRect?.Invoke(r, m, controlID);
        }

        static NodeEditor()
        {
            s_internalAddCursorRect = (Refl_AddCursorRect?)
                typeof(EditorGUIUtility)
                    .GetMethod("Internal_AddCursorRect", BindingFlags.NonPublic | BindingFlags.Static)?
                    .CreateDelegate(typeof(Refl_AddCursorRect));
        }

        private static readonly Refl_AddCursorRect? s_internalAddCursorRect;
        delegate void Refl_AddCursorRect(Rect r, MouseCursor m, int controlID);
    }
}
