using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// Per-MapperClass graph-walk + dedup pass for [Map(DeepClone=true, CycleSafe=true)]
/// declarations. Collects the set of reference types reachable from each declaration's
/// destination chain and classifies each: parameterless ctor (gets a clone helper) vs
/// primary-ctor-only (inline construction if acyclic, ZAMP021 if cyclic).
/// </summary>
internal static class CycleSafeDeepCloneEmitter
{
    /// <summary>
    /// Per-type metadata produced by <see cref="CollectReachableTypes"/>.
    /// </summary>
    internal sealed record ReachableTypeInfo(
        INamedTypeSymbol Type,
        bool HasParameterlessCtor,
        bool IsCyclic);

    /// <summary>
    /// Walks the reachable type graph from every [Map(DeepClone=true, CycleSafe=true)]
    /// declaration in <paramref name="cls"/>. Returns the deduped set keyed by type symbol
    /// (SymbolEqualityComparer.Default). Skips: value types, strings, types covered by an
    /// explicit nested [Map&lt;,&gt;] (those already have a Map(src, tracker) overload).
    /// </summary>
    /// <param name="diagnosticSink">Callback fired for each ZAMP021 to report. Caller decides
    /// where the diagnostics are routed (typically through the MappingGenerator's
    /// SourceProductionContext.ReportDiagnostic).</param>
    public static Dictionary<INamedTypeSymbol, ReachableTypeInfo> CollectReachableTypes(
        MapperClass cls,
        Compilation comp,
        System.Action<Diagnostic>? diagnosticSink = null)
    {
        var collected = new Dictionary<INamedTypeSymbol, ReachableTypeInfo>(SymbolEqualityComparer.Default);

        foreach (var decl in cls.Mappings)
        {
            if (!(decl.DeepClone && decl.CycleSafe)) continue;

            var dst = comp.GetTypeByMetadataName(StripGlobal(decl.DestinationTypeFqn));
            if (dst is null) continue;

            var visiting = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            WalkType(dst, cls, comp, collected, visiting, decl, diagnosticSink);
        }

        return collected;
    }

    private static void WalkType(
        INamedTypeSymbol type,
        MapperClass cls,
        Compilation comp,
        Dictionary<INamedTypeSymbol, ReachableTypeInfo> collected,
        HashSet<INamedTypeSymbol> visiting,
        MappingDecl originatingDecl,
        System.Action<Diagnostic>? diagnosticSink)
    {
        // Stop at: value type / string. Caller filters these before calling, but defensive here.
        if (type.IsValueType || type.SpecialType == SpecialType.System_String) return;

        // Already in current walking stack → cycle detected.
        if (visiting.Contains(type))
        {
            var hasParameterless = type.InstanceConstructors.Any(c =>
                c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            if (!hasParameterless && diagnosticSink is not null)
            {
                diagnosticSink(Diagnostic.Create(
                    Diagnostics.ZAMP021_DeepCloneCycleSafePrimaryCtorCycle,
                    location: null,
                    originatingDecl.SourceTypeFqn,
                    originatingDecl.DestinationTypeFqn,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
            return;
        }

        // Already processed in a previous walk (different originating decl, same type) → skip.
        if (collected.ContainsKey(type)) return;

        var hasParameterlessCtor = type.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

        visiting.Add(type);
        try
        {
            // Walk public property graph.
            foreach (var prop in PropertyMatcher.GetAllPublicProperties(type))
            {
                if (PropertyMatcher.IsObsolete(prop)) continue;
                var propType = prop.Type;

                // Skip value types / strings.
                if (propType.IsValueType || propType.SpecialType == SpecialType.System_String) continue;

                // Collection: walk element type.
                var coll = NestedMappingResolver.AsCollection(propType);
                if (coll is not null)
                {
                    if (coll.Value.Element is INamedTypeSymbol elemNt)
                    {
                        if (elemNt.IsValueType || elemNt.SpecialType == SpecialType.System_String) continue;
                        // Skip if explicit nested [Map<elem, elem>] exists.
                        if (NestedMappingResolver.FindNestedMapper(cls, elemNt, elemNt) is not null) continue;
                        WalkType(elemNt, cls, comp, collected, visiting, originatingDecl, diagnosticSink);
                    }
                    continue;
                }

                // Skip if explicit nested [Map<propType, propType>] exists in the MapperClass.
                if (propType is INamedTypeSymbol propNt)
                {
                    if (NestedMappingResolver.FindNestedMapper(cls, propNt, propNt) is not null) continue;
                    WalkType(propNt, cls, comp, collected, visiting, originatingDecl, diagnosticSink);
                }
            }

            // Add THIS type to collected (after walking children so cycle detection above already fired).
            collected[type] = new ReachableTypeInfo(
                type,
                HasParameterlessCtor: hasParameterlessCtor,
                IsCyclic: false /* cycle case returned early; non-cyclic types reach here */);
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static string StripGlobal(string fqn) =>
        fqn.StartsWith("global::", System.StringComparison.Ordinal) ? fqn.Substring(8) : fqn;
}
