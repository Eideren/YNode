using System;

namespace YNode
{
    /// <summary> Mark a serializable field as an output port. You can access this through NodeEditor.ActivePorts </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class OutputAttribute : IOAttribute
    {
        /// <summary> Mark a serializable field as an output port. You can access this through NodeEditor.ActivePorts </summary>
        public OutputAttribute() { }
    }
}
