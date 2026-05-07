using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal sealed record PropertyMapping(
    string TargetParamName,
    string SourcePropertyName,
    ITypeSymbol SourceType,
    ITypeSymbol TargetType);

internal sealed record MatchResult(
    IMethodSymbol Constructor,
    System.Collections.Generic.IReadOnlyList<PropertyMapping> Mappings,
    System.Collections.Generic.IReadOnlyList<string> UnmatchedTargetParams);

internal static class PropertyMatcher
{
    public static MatchResult? Match(INamedTypeSymbol source, INamedTypeSymbol destination)
    {
        var ctor = PickConstructor(destination);
        if (ctor is null) return null;

        var sourceProps = source.GetMembers().OfType<IPropertySymbol>()
            .Where(p => p.DeclaredAccessibility == Accessibility.Public)
            .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

        var mappings = new System.Collections.Generic.List<PropertyMapping>();
        var unmatched = new System.Collections.Generic.List<string>();

        foreach (var p in ctor.Parameters)
        {
            if (sourceProps.TryGetValue(p.Name, out var srcProp))
            {
                mappings.Add(new PropertyMapping(
                    TargetParamName: p.Name,
                    SourcePropertyName: srcProp.Name,
                    SourceType: srcProp.Type,
                    TargetType: p.Type));
            }
            else
            {
                unmatched.Add(p.Name);
            }
        }

        return new MatchResult(ctor, mappings, unmatched);
    }

    private static IMethodSymbol? PickConstructor(INamedTypeSymbol type)
    {
        var candidates = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .Where(c => !IsCopyConstructor(c, type))
            .ToList();

        if (candidates.Count == 0) return null;
        return candidates.OrderByDescending(c => c.Parameters.Length).First();
    }

    private static bool IsCopyConstructor(IMethodSymbol ctor, INamedTypeSymbol owner) =>
        ctor.Parameters.Length == 1 &&
        SymbolEqualityComparer.Default.Equals(ctor.Parameters[0].Type, owner);
}
