namespace ZeroAlloc.Mapping;

/// <summary>
/// Marker — when applied to a <c>[Map]</c>/<c>[TryMap]</c>-decorated <c>static partial class</c>,
/// substitutes the named culture for <c>CultureInfo.InvariantCulture</c> in every emitted
/// <c>Parse(string, IFormatProvider)</c> and <c>ToString(IFormatProvider)</c> call site on
/// that class. The string is passed verbatim to <c>CultureInfo.GetCultureInfo</c> at runtime;
/// invalid names throw <c>CultureNotFoundException</c> on first call.
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MappingCultureAttribute : System.Attribute
{
    public MappingCultureAttribute(string cultureName)
    {
        CultureName = cultureName;
    }

    public string CultureName { get; }
}
