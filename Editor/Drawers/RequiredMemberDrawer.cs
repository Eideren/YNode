using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEngine;

namespace YNode.Editor
{
    [DrawerPriority(100, 0, 0)]
    public class RequiredMemberDrawer : OdinAttributeDrawer<RequiredMemberAttribute>
    {
        private static readonly Dictionary<Type, object> defaultObj = new();

        protected override void DrawPropertyLayout(GUIContent? label)
        {
            object? comparer = null;
            var baseType = Property.ValueEntry.BaseValueType;
            if (baseType.IsValueType && defaultObj.TryGetValue(baseType, out comparer) == false)
                defaultObj[baseType] = comparer = Activator.CreateInstance(baseType);

            if (Property.ValueEntry.WeakSmartValue == comparer)
                SirenixEditorGUI.ErrorMessageBox($"{Property.NiceName} is required");

            CallNextDrawer(label);
        }
    }
}
