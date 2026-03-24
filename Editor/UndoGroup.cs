using System;
using UnityEditor;

namespace YNode.Editor
{
    public struct UndoGroup : IDisposable
    {
        private readonly int _group;

        public UndoGroup(string name)
        {
            Undo.SetCurrentGroupName(name);
            _group = Undo.GetCurrentGroup();
        }

        public void Dispose()
        {
            Undo.CollapseUndoOperations(_group);
        }
    }
}
