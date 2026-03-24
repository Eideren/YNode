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
        public const float Size = 16;

        private INodeValue? _previouslyConnected;
        private CanConnectTo _canConnectTo;
        private GetConnected _getConnected;
        private SetConnection _setConnection;

        public IO Direction { get; }
        public string FieldName { get; }
        public NodeEditor NodeEditor { get; }
        public Type ValueType { get; }
        public string Tooltip { get; set; }
        public NoodleStroke Stroke { get; set; }
        public float LocalYOffset { get; set; }

        public Rect CachedRect
        {
            get
            {
                Vector2 portHandlePos = NodeEditor.Value.Position;
                if (Direction == IO.Output)
                    portHandlePos.x += NodeEditor.CachedSize.x;
                portHandlePos.y += LocalYOffset;
                return new Rect(portHandlePos.x - Size / 2f, portHandlePos.y - Size / 2f, Size, Size);
            }
        }

        public INodeValue? Connected => SampleConnected();

        public NodeEditor? ConnectedEditor
        {
            get
            {
                INodeValue? value = SampleConnected();
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

        public bool TryReuseFor(string fieldName, NodeEditor nodeEditorParam, Type type, IO direction, GetConnected getConnected, CanConnectTo canConnectTo, SetConnection setConnection, NoodleStroke stroke, string? tooltip = null)
        {
            if (FieldName == fieldName &&
                ValueType == type &&
                Direction == direction &&
                NodeEditor == nodeEditorParam &&
                Stroke == stroke &&
                Tooltip == (tooltip ?? ValueType.Name))
            {
                _getConnected = getConnected;
                _canConnectTo = canConnectTo;
                _setConnection = setConnection;
                SampleConnected(); // Updates LooselyConnectedToThis
                return true;
            }

            return false;
        }

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
            SampleConnected(); // Updates LooselyConnectedToThis
        }

        public bool CanConnectTo(Type type) => _canConnectTo(type);

        public bool TryConnectTo(NodeEditor newConnection, bool undo) => TryConnectTo(newConnection.Value, undo);

        public bool TryConnectTo(INodeValue expectedValue, bool undo)
        {
            if (Connected == expectedValue)
            {
                Debug.LogWarning("Port already connected. ");
                return false;
            }

            if (CanConnectTo(expectedValue.GetType()) == false)
                return false;

            if (undo)
                Undo.RegisterCompleteObjectUndo(NodeEditor.Graph, "Connect Port");

            _setConnection(expectedValue);
            var currentValue = SampleConnected();
            return ReferenceEquals(currentValue, expectedValue);
        }

        /// <summary> Disconnect this port from another port </summary>
        public void Disconnect(bool undo)
        {
            if (undo)
                Undo.RecordObject(NodeEditor.Graph, "Disconnect Port");

            _setConnection(null);
            if (_previouslyConnected != null && NodeEditor.Window.NodesToEditor.TryGetValue(_previouslyConnected, out var editor))
            {
                editor.LooselyConnectedToThis.Remove(this);
                _previouslyConnected = null;
            }
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

        public void MarkRecycled()
        {
            _canConnectTo = null!;
            _getConnected = null!;
            _setConnection = null!;
        }

        public INodeValue? SampleConnected()
        {
            var newConnected = _getConnected();
            if (ReferenceEquals(newConnected, _previouslyConnected) == false)
            {
                if (_previouslyConnected != null && NodeEditor.Window.NodesToEditor.TryGetValue(_previouslyConnected, out var editor))
                {
                    editor.LooselyConnectedToThis.Remove(this);
                }

                if (newConnected != null && NodeEditor.Window.NodesToEditor.TryGetValue(newConnected, out editor))
                {
                    editor.LooselyConnectedToThis.Add(this);
                }
            }

            return newConnected;
        }
    }
}
