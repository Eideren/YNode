using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEditor;
using UnityEngine;

namespace YNode.Editor
{
    public delegate INodeValue? GetConnected();

    public delegate bool CanConnectTo(Type type);

    public delegate void SetConnection(INodeValue? value);

    public class Port
    {
        private readonly CanConnectTo _canConnectTo;

        private readonly GetConnected _getConnected;

        private readonly SetConnection _setConnection;

        public IO Direction { get; }
        public string FieldName { get; }
        public NodeEditor NodeEditor { get; }
        public Type ValueType { get; }
        public string Tooltip { get; set; }
        public NoodleStroke Stroke { get; set; }
        public Rect CachedRect { get; set; }
        public float CachedHeight { get; set; }

        public INodeValue? Connected => _getConnected();

        public NodeEditor? ConnectedEditor
        {
            get
            {
                INodeValue? value = _getConnected();
                if (value != null)
                {
                    if (NodeEditor.Window.NodesToEditor.TryGetValue(value, out var editor))
                        return editor;
                    else
                        Debug.LogError($"Value without editor:{value}");
                }

                return null;
            }
        }

        /// <summary> Construct a dynamic port. Dynamic ports are not forgotten on reimport, and is ideal for runtime-created ports. </summary>
        public Port(string fieldName, NodeEditor nodeEditorParam, Type type, IO direction, GetConnected getConnected, CanConnectTo canConnectTo, SetConnection setConnection, NoodleStroke stroke, string? tooltip = null)
        {
            FieldName = fieldName;
            ValueType = type;
            Direction = direction;
            NodeEditor = nodeEditorParam;
            _getConnected = getConnected;
            _canConnectTo = canConnectTo;
            _setConnection = setConnection;
            Stroke = stroke;
            Tooltip = tooltip ?? ValueType.Name;
        }

        /// <summary> Connect this <see cref="Port" /> to another </summary>
        public void Connect(NodeEditor newConnection, bool undo)
        {
            if (Connected == newConnection.Value)
            {
                Debug.LogWarning("Port already connected. ");
                return;
            }

            if (undo)
                Undo.RecordObjects(new[] { NodeEditor, newConnection }, "Connect Port");

            _setConnection(newConnection.Value);
        }

        public bool CanConnectTo(Type type) => _canConnectTo(type);

        /// <summary> Disconnect this port from another port </summary>
        public void Disconnect(bool undo)
        {
            if (undo)
                Undo.RecordObject(NodeEditor, "Disconnect Port");
            _setConnection(null);
        }

        /// <summary> Get reroute points for a given connection. This is used for organization </summary>
        public List<Vector2> GetReroutePoints()
        {
            if (NodeEditor.ReroutePoints.TryGetValue(FieldName, out var points) == false)
                NodeEditor.ReroutePoints[FieldName] = points = new List<Vector2>();
            return points;
        }

        public bool TryGetReroutePoints([MaybeNullWhen(false)] out List<Vector2> points)
        {
            return NodeEditor.ReroutePoints.TryGetValue(FieldName, out points);
        }

        public void ClearReroute()
        {
            NodeEditor.ReroutePoints.Remove(FieldName);
        }
    }
}
