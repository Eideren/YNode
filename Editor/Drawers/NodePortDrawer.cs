using System;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace YNode.Editor
{
    // Must be above one of the polymorphic drawer having special handling for fields pointing to the same target
    [DrawerPriority(90, 0, 0)]
    public sealed class NodePortDrawer<T> : OdinAttributeDrawer<T>, IDisposable where T : IOAttribute
    {
        private Port? _port = null;
        private SerializedProperty? _prop;

        protected override bool CanDrawAttributeProperty(InspectorProperty property)
        {
            return property.Tree.WeakTargets[0] is NodeEditor && typeof(INodeValue).IsAssignableFrom(property.Info.TypeOfValue);
        }

        protected override void Initialize()
        {
            base.Initialize();
            var node = (NodeEditor)Property.Tree.WeakTargets[0];
            _prop = node.SerializedObject.FindProperty(Property.UnityPropertyPath);
            if (_prop is null)
            {
                Debug.LogWarning($"Could not find {Property.UnityPropertyPath} in {node}");
                return;
            }

            if (node.ActivePorts.ContainsKey(Property.UnityPropertyPath))
            {
                Debug.LogWarning("Multiple drawer for the same port ?");
                return;
            }

            var valueType = Property.Info.TypeOfValue;
            string tooltip = Property.GetAttribute<TooltipAttribute>()?.tooltip ?? valueType.Name;
            var attrib = Property.Attributes.GetAttribute<IOAttribute>();
            var io = attrib is OutputAttribute ? IO.Output : IO.Input;
            _port = node.AddPort(Property.UnityPropertyPath, valueType, io, GetConnected, CanConnectTo, SetConnection, attrib.Stroke, tooltip);
            node.Window.Repaint();

            void SetConnection(INodeValue? newConnection)
            {
                // We're going through unity's serialization stuff instead of Odin's
                // As odin is latent when it comes to assigning polymorphic fields to a type of value that's different from the existing one

                _prop.managedReferenceValue = newConnection;
                node.SerializedObject.ApplyModifiedProperties();
            }

            INodeValue? GetConnected()
            {
                node.SerializedObject.UpdateIfRequiredOrScript();
                return (INodeValue?)_prop.managedReferenceValue;
            }

            bool CanConnectTo(Type type) => valueType.IsAssignableFrom(type);
        }

        public void Dispose()
        {
            if (GraphWindow.InNodeEditor) // We only care about dispose caused by changes in properties, other kinds should be handled by the graph editor
            {
                var node = (NodeEditor)Property.Tree.WeakTargets[0];
                node.RemovePort(Property.UnityPropertyPath, false, false);
            }
        }

        protected override void DrawPropertyLayout(GUIContent label)
        {
            if (!GraphWindow.InNodeEditor)
            {
                CallNextDrawer(label);
                return;
            }

            if (_prop is null)
            {
                return;
            }

            Port port = _port ?? throw new NullReferenceException();
            var node = (NodeEditor)Property.Tree.WeakTargets[0];
            node.ActivePorts[Property.UnityPropertyPath] = port;

            if (Property.Tree.WeakTargets.Count > 1)
            {
                SirenixEditorGUI.WarningMessageBox("Cannot draw ports with multiple nodes selected");
                return;
            }

            LabelWidthAttribute? labelWidth = Property.GetAttribute<LabelWidthAttribute>();
            if (labelWidth != null)
                GUIHelper.PushLabelWidth(labelWidth.Width);

            PropertyField(label, port);

            if (labelWidth != null)
                GUIHelper.PopLabelWidth();
        }

        /// <summary> Make a field for a serialized property. Manual node port override. </summary>
        private void PropertyField(GUIContent? label, Port port)
        {
            if (Property.Info.GetAttribute<RequiredAttribute>() is not null && Property.BaseValueEntry.WeakSmartValue == null)
                SirenixEditorGUI.ErrorMessageBox($"{Property.NiceName} is required");

            Rect rect = new();
            // If property is an input, display a regular property field and put a port handle on the left side
            if (port.Direction == IO.Input)
            {
                // Get data from [Input] attribute
                bool usePropertyAttributes = true;

                float spacePadding = 0;
                foreach (Attribute? attr in Property.Attributes)
                {
                    if (attr is SpaceAttribute spaceAttribute)
                    {
                        if (usePropertyAttributes)
                            GUILayout.Space(spaceAttribute.height);
                        else
                            spacePadding += spaceAttribute.height;
                    }
                    else if (attr is HeaderAttribute headerAttribute)
                    {
                        if (usePropertyAttributes)
                        {
                            //GUI Values are from https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/ScriptAttributeGUI/Implementations/DecoratorDrawers.cs
                            Rect position = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight * 1.5f - EditorGUIUtility.standardVerticalSpacing); //Layout adds standardVerticalSpacing after rect so we subtract it.
                            position.yMin += EditorGUIUtility.singleLineHeight * 0.5f;
                            position = EditorGUI.IndentedRect(position);
                            GUI.Label(position, headerAttribute.header, EditorStyles.boldLabel);
                        }
                        else
                            spacePadding += EditorGUIUtility.singleLineHeight * 1.5f;
                    }
                }

                if (Property.GetAttribute<HideLabelAttribute>() is not null || label == null)
                    EditorGUILayout.Space(0f);
                else
                    EditorGUILayout.LabelField(label);

                rect = GUILayoutUtility.GetLastRect();
                GraphWindow.Current.GetPortStyle(port, out _, out _, out var paddingLeft);
                rect.position = rect.position - new Vector2(Port.Size + paddingLeft, -spacePadding);
                // If property is an output, display a text label and put a port handle on the right side
            }
            else if (port.Direction == IO.Output)
            {
                // Get data from [Output] attribute

                bool usePropertyAttributes = true;

                float spacePadding = 0;
                foreach (Attribute? attr in Property.Attributes)
                {
                    if (attr is SpaceAttribute spaceAttribute)
                    {
                        if (usePropertyAttributes)
                            GUILayout.Space(spaceAttribute.height);
                        else
                            spacePadding += spaceAttribute.height;
                    }
                    else if (attr is HeaderAttribute headerAttribute)
                    {
                        if (usePropertyAttributes)
                        {
                            //GUI Values are from https://github.com/Unity-Technologies/UnityCsReference/blob/master/Editor/Mono/ScriptAttributeGUI/Implementations/DecoratorDrawers.cs
                            Rect position = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight * 1.5f - EditorGUIUtility.standardVerticalSpacing); //Layout adds standardVerticalSpacing after rect so we subtract it.
                            position.yMin += EditorGUIUtility.singleLineHeight * 0.5f;
                            position = EditorGUI.IndentedRect(position);
                            GUI.Label(position, headerAttribute.header, EditorStyles.boldLabel);
                        }
                        else
                            spacePadding += EditorGUIUtility.singleLineHeight * 1.5f;
                    }
                }

                if (Property.GetAttribute<HideLabelAttribute>() is not null || label == null)
                    EditorGUILayout.Space(0f);
                else
                    EditorGUILayout.LabelField(label, Resources.OutputPort, GUILayout.MinWidth(30));

                rect = GUILayoutUtility.GetLastRect();
                GraphWindow.Current.GetPortStyle(port, out _, out _, out float padding);
                rect.width += padding;
                rect.position = rect.position + new Vector2(rect.width, spacePadding);
            }

            rect.size = new(Port.Size, Port.Size);

            // Register the handle position
            if (Event.current.type == EventType.Repaint)
                port.LocalYOffset = rect.center.y;
        }
    }
}
