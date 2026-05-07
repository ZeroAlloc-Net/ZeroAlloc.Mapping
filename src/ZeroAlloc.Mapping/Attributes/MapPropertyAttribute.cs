namespace ZeroAlloc.Mapping;

/// <summary>
/// Renames a single source→destination property pair on a generated mapping method.
/// Multiple instances may decorate the same generated method to rename multiple property pairs.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class MapPropertyAttribute : System.Attribute
{
    public MapPropertyAttribute(string sourceProperty, string targetProperty)
    {
        SourceProperty = sourceProperty;
        TargetProperty = targetProperty;
    }

    public string SourceProperty { get; }
    public string TargetProperty { get; }
}
