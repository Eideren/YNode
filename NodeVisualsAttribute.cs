using System;
using UnityEngine;

namespace YNode
{
    /// <summary> Define custom visuals for this node type </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class NodeVisualsAttribute : Attribute
    {
        public const int DefaultWidth = 208;

        public Color? Tint;
        public int Width = 208;
        public string? Icon = null;

        public NodeVisualsAttribute() { }

        public NodeVisualsAttribute(float r, float g, float b)
        {
            Tint = new Color(r, g, b);
        }

        public NodeVisualsAttribute(int r, int g, int b)
        {
            Tint = new Color(r / 255f, g / 255f, b / 255f);
        }
    }
}
