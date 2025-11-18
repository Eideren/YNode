using Sirenix.OdinInspector.Editor.Validation;

[assembly: RegisterValidator(typeof(RequiredMemberAttributeValidator<>))]

public class RequiredMemberAttributeValidator<T> : AttributeValidator<System.Runtime.CompilerServices.RequiredMemberAttribute, T>// where T : class
{
    protected override void Validate(ValidationResult result)
    {
        var d = Property.ValueEntry.WeakSmartValue;
        switch (d)
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
                if (d.Equals(default(T)))
                    goto case null;

                return;
            }
        }
    }
}
