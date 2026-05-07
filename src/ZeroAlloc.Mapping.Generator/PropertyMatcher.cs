using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal sealed record PropertyMapping(
    string TargetParamName,
    string SourcePropertyName,           // dot-separated for flattening, single name otherwise
    ITypeSymbol SourceType,              // type of the leaf property
    ITypeSymbol TargetType,
    bool IsFlattened = false);

internal sealed record ConstantMapping(
    string TargetParamName,
    object? Value,
    ITypeSymbol TargetType);

internal sealed record MatchResult(
    IMethodSymbol Constructor,
    System.Collections.Generic.IReadOnlyList<PropertyMapping> Mappings,
    System.Collections.Generic.IReadOnlyList<ConstantMapping> Constants,
    System.Collections.Generic.IReadOnlyList<string> UnmatchedTargetParams);

internal static class PropertyMatcher
{
    internal static bool IsObsolete(ISymbol s) =>
        s.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "ObsoleteAttribute" } ac &&
            ac.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true });

    public static MatchResult? Match(INamedTypeSymbol source, INamedTypeSymbol destination, IMethodSymbol? userPartial = null, bool caseInsensitive = false)
    {
        var ctor = PickConstructor(destination);
        if (ctor is null) return null;

        var nameComparer = caseInsensitive ? System.StringComparer.OrdinalIgnoreCase : System.StringComparer.Ordinal;

        var sourceProps = new System.Collections.Generic.Dictionary<string, IPropertySymbol>(nameComparer);
        foreach (var p in source.GetMembers().OfType<IPropertySymbol>())
        {
            if (p.DeclaredAccessibility != Accessibility.Public) continue;
            if (IsObsolete(p)) continue;
            // Last-writer-wins on duplicate (case-insensitive collision); ZAMP011 reports the
            // ambiguity separately so the generator does not crash on otherwise-valid inputs.
            sourceProps[p.Name] = p;
        }

        var destProps = new System.Collections.Generic.Dictionary<string, IPropertySymbol>(nameComparer);
        foreach (var p in destination.GetMembers().OfType<IPropertySymbol>())
        {
            if (p.DeclaredAccessibility != Accessibility.Public) continue;
            destProps[p.Name] = p;
        }

        var renames = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.Ordinal);
        var constants = new System.Collections.Generic.Dictionary<string, object?>(System.StringComparer.Ordinal);

        if (userPartial is not null)
        {
            foreach (var attr in userPartial.GetAttributes())
            {
                var name = attr.AttributeClass?.Name;
                if (name == "MapPropertyAttribute" && attr.ConstructorArguments.Length == 2)
                {
                    var sourceProp = attr.ConstructorArguments[0].Value as string;
                    var targetProp = attr.ConstructorArguments[1].Value as string;
                    if (sourceProp is not null && targetProp is not null)
                        renames[targetProp] = sourceProp;
                }
                else if (name == "MapValueAttribute" && attr.ConstructorArguments.Length == 2)
                {
                    var targetProp = attr.ConstructorArguments[0].Value as string;
                    if (targetProp is not null)
                        constants[targetProp] = attr.ConstructorArguments[1].Value;
                }
            }
        }

        var mappings = new System.Collections.Generic.List<PropertyMapping>();
        var constMappings = new System.Collections.Generic.List<ConstantMapping>();
        var unmatched = new System.Collections.Generic.List<string>();

        foreach (var p in ctor.Parameters)
        {
            // [Obsolete] dest param — silent skip, don't add to unmatched.
            // Also check the matching destination property: for record positional params,
            // [property: Obsolete] targets only the synthesized property, not the parameter.
            if (IsObsolete(p)) continue;
            if (destProps.TryGetValue(p.Name, out var destProp) && IsObsolete(destProp)) continue;
            if (constants.TryGetValue(p.Name, out var constValue))
            {
                constMappings.Add(new ConstantMapping(p.Name, constValue, p.Type));
                continue;
            }

            var sourceName = renames.TryGetValue(p.Name, out var rename) ? rename : p.Name;

            if (sourceName.Contains('.'))
            {
                var (leaf, leafType) = WalkDottedPath(source, sourceName);
                if (leaf is null || leafType is null)
                {
                    unmatched.Add(p.Name);
                    continue;
                }
                mappings.Add(new PropertyMapping(
                    TargetParamName: p.Name,
                    SourcePropertyName: sourceName,
                    SourceType: leafType,
                    TargetType: p.Type,
                    IsFlattened: true));
                continue;
            }

            if (sourceProps.TryGetValue(sourceName, out var srcProp))
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

        return new MatchResult(ctor, mappings, constMappings, unmatched);
    }

    private static (IPropertySymbol? Leaf, ITypeSymbol? LeafType) WalkDottedPath(INamedTypeSymbol root, string dottedPath)
    {
        INamedTypeSymbol? cursor = root;
        IPropertySymbol? leaf = null;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (cursor is null) return (null, null);
            leaf = cursor.GetMembers(segment).OfType<IPropertySymbol>()
                .FirstOrDefault(p => p.DeclaredAccessibility == Accessibility.Public);
            if (leaf is null) return (null, null);
            cursor = leaf.Type as INamedTypeSymbol;
        }
        return (leaf, leaf?.Type);
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
