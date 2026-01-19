using Sirenix.OdinInspector.Editor;
using Sirenix.OdinInspector.Editor.Validation;

[assembly: RegisterValidator(typeof(RequiredMemberAttributeValidator<>))]

public class RequiredMemberAttributeValidator<T> : AttributeValidator<System.Runtime.CompilerServices.RequiredMemberAttribute, T>
{
    public override bool CanValidateProperty(InspectorProperty property)
    {
        if (base.CanValidateProperty(property))
        {
            // Looks like odin has a bug where fields may receive a member required attribute even though they are not marked as such
            return property.Info.GetAttribute<System.Runtime.CompilerServices.RequiredMemberAttribute>() is not null;
        }

        return false;
    }

    protected override void Validate(ValidationResult result)
    {
        object? weakValue = Property.ValueEntry.WeakSmartValue;
        switch (weakValue)
        {
            case UnityEngine.Object o:
            {
                if (o == null)
                    goto case null;

                return;
            }
            case string s:
            {
                if (string.IsNullOrEmpty(s))
                    goto case null;

                return;
            }
            case null:
            {
                result.AddError($"'{Property.NiceName}' must have a value");
                return;
            }
            default:
            {
                if (weakValue.Equals(default(T)))
                    goto case null;

                return;
            }
        }
    }
}
