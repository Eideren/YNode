using System;

namespace YNode
{
    /// <summary> Mark a serializable field as an input port. You can access this through NodeEditor.ActivePorts </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class InputAttribute : IOAttribute
    {
        /// <summary> Mark a serializable field as an input port. You can access this through NodeEditor.ActivePorts </summary>
        public InputAttribute() { }
    }
}
