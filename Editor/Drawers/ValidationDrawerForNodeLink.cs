using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.Validation;

namespace YNode.Editor
{
    [DrawerPriority(91, 0, 0.0)]
    public class ValidationDrawerForNodeLink<T> : ValidationDrawer<T>
    {
        protected override bool CanDrawValueProperty(InspectorProperty property)
        {
            return property.Tree.WeakTargets[0] is NodeEditor
                   && typeof(INodeValue).IsAssignableFrom(property.Info.TypeOfValue)
                   && property.GetAttribute<IOAttribute>() is not null
                   && base.CanDrawValueProperty(property);
        }
    }
}
