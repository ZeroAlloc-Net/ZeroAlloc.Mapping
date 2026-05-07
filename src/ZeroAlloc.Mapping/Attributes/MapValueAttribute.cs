namespace ZeroAlloc.Mapping;

/// <summary>
/// Assigns a constant value to a destination property on a generated mapping method.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapValueAttribute : System.Attribute
{
    public MapValueAttribute(string targetProperty, object value)
    {
        TargetProperty = targetProperty;
        Value = value;
    }

    public string TargetProperty { get; }
    public object Value { get; }
}
