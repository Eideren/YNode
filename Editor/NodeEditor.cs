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

            var port = new Port(fieldName, this, type, direction, getConnected, canConnectTo, setConnection, stroke, tooltip);
            _ports.Add(fieldName, port);
            return port;
        }


        /// <summary> Remove a dynamic port from the node </summary>
        public void RemovePort(string fieldName, bool disconnect)
        {
            Port? dynamicPort = GetPort(fieldName);
            if (dynamicPort == null) throw new ArgumentException($"port {fieldName} doesn't exist");
            RemovePort(dynamicPort, disconnect);
        }

        /// <summary> Remove a dynamic port from the node </summary>
        public void RemovePort(Port port, bool disconnect)
        {
            if (disconnect)
                port.Disconnect();
            _ports.Remove(port.FieldName);
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
        public void ClearConnections()
        {
            foreach ((_, Port port) in _ports)
                port.Disconnect();
        }

        public virtual void PreRemoval() { }

        public virtual void OnHeaderGUI()
        {
            _title ??= ObjectNames.NicifyVariableName(Value.GetType().Name);
            GUILayout.Label(_title, Resources.Styles.NodeHeader, GUILayout.Height(TitleHeight));
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
            const string ID = "NodeEditor_DrawEditableTitle";

            var c = new GUIContent(title);
            Resources.Styles.NodeHeader.CalcMinMaxWidth(c, out float minWidth, out float maxWidth);
            minWidth = MathF.Max(10, minWidth);

            var titleRect = GUILayoutUtility.GetRect(minWidth, minWidth, TitleHeight, TitleHeight);
            var center = titleRect.center;
            titleRect.xMin = center.x - minWidth / 2f;
            titleRect.xMax = center.x + minWidth / 2f;

            AddCursorRectFromBody(titleRect, MouseCursor.Text);
            var e = Event.current;
            if (e.clickCount == 2 && e.button == 0 && titleRect.Contains(e.mousePosition))
            {
                e.Use();
                Window.CurrentActivity = new EditTitleActivity(this, Window);
            }

            if (Window.CurrentActivity is EditTitleActivity edit && edit.Editor == this)
            {
                GUI.SetNextControlName(ID);
                title = EditorGUI.TextField(titleRect, title, Resources.Styles.NodeHeader);

                if (GUI.GetNameOfFocusedControl() == ID && EditorGUIUtility.editingTextField) // Successfully swapped focus
                    edit.Focused = true;
                else if (edit.Focused == false) // Not in focus and has never been, set the focus
                    EditorGUI.FocusTextInControl(ID);
                else if (edit.Focused) // Not in focus right now but has been before, user swapped focus, close off activity
                    Window.CurrentActivity = null;
            }
            else
            {
                GUI.Label(titleRect, title, Resources.Styles.NodeHeader);
            }
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

        public class EditTitleActivity : NodeActivity
        {
            public NodeEditor Editor;
            public bool Focused = false;

            public EditTitleActivity(NodeEditor editor, GraphWindow window) : base(window) => Editor = editor;

            public override void InputPreDraw(Event e) { }

            public override void PreNodeDraw() { }

            public override void PostNodeDraw() { }

            public override void InputPostDraw(Event e) { }
        }
    }
}
