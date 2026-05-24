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

    /// <summary>
    /// Emits one <c>private static T __CloneCycleSafe_T(T src, IDictionary&lt;object, object&gt; tracker)</c>
    /// helper per parameterless-ctor type in <paramref name="collected"/>. Primary-ctor-only types
    /// (HasParameterlessCtor = false) get NO helper — they're handled inline at the call site by
    /// <see cref="InlinePrimaryCtorClone"/>.
    /// </summary>
    public static void EmitClonerHelpers(
        StringBuilder sb,
        MapperClass cls,
        Compilation comp,
        Dictionary<INamedTypeSymbol, ReachableTypeInfo> collected)
    {
        foreach (var kv in collected)
        {
            if (!kv.Value.HasParameterlessCtor) continue;
            EmitHelper(sb, kv.Value, cls, comp, collected);
        }
    }

    private static void EmitHelper(
        StringBuilder sb,
        ReachableTypeInfo info,
        MapperClass cls,
        Compilation comp,
        Dictionary<INamedTypeSymbol, ReachableTypeInfo> collected)
    {
        var type = info.Type;
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var mangled = MangleTypeNameForCall(type);

        sb.Append("    private static ").Append(fqn).Append(" __CloneCycleSafe_").Append(mangled)
          .Append("(").Append(fqn).Append(" src, global::System.Collections.Generic.IDictionary<object, object> tracker)\n");
        sb.Append("    {\n");
        sb.Append("        if (tracker.TryGetValue(src, out var __cached)) return (").Append(fqn).Append(")__cached;\n");
        sb.Append("        var __new = new ").Append(fqn).Append("();\n");
        sb.Append("        tracker.Add(src, __new);\n");

        foreach (var prop in PropertyMatcher.GetAllPublicProperties(type))
        {
            if (PropertyMatcher.IsObsolete(prop)) continue;
            if (prop.SetMethod is not { DeclaredAccessibility: Accessibility.Public } setter) continue;
            if (setter.IsInitOnly) continue;

            var srcAccess = "src." + prop.Name;
            sb.Append("        __new.").Append(prop.Name).Append(" = ");
            EmitPropertyAssignment(sb, srcAccess, prop.Type, cls, comp, collected);
            sb.Append(";\n");
        }

        sb.Append("        return __new;\n");
        sb.Append("    }\n");
    }

    /// <summary>
    /// Emits the right-hand expression for a single property assignment in a
    /// <c>__CloneCycleSafe_T</c> helper body. Routes to:
    /// <list type="bullet">
    ///   <item>value/string → direct (no clone needed; ConversionResolver handles primitives).</item>
    ///   <item>explicit nested [Map&lt;,&gt;] → <c>Map(srcExpr, tracker)</c>.</item>
    ///   <item>collection → ToArray/ToList over element-wise clone calls.</item>
    ///   <item>reference type with parameterless ctor → <c>__CloneCycleSafe_T(srcExpr, tracker)</c>.</item>
    ///   <item>reference type without parameterless ctor (primary-ctor-only) → inline
    ///     <c>new T(arg1: ..., arg2: ...)</c>. ZAMP021 already fired if this is part of a cycle.</item>
    /// </list>
    /// </summary>
#pragma warning disable MA0051
    private static void EmitPropertyAssignment(
        StringBuilder sb,
        string srcExpr,
        ITypeSymbol propType,
        MapperClass cls,
        Compilation comp,
        Dictionary<INamedTypeSymbol, ReachableTypeInfo> collected)
    {
        if (propType.IsValueType || propType.SpecialType == SpecialType.System_String)
        {
            var conv = ConversionResolver.Resolve(propType, propType, comp);
            sb.Append(ConversionResolver.Apply(conv, srcExpr, propType, cls.Culture));
            return;
        }

        // Collection case.
        var coll = NestedMappingResolver.AsCollection(propType);
        if (coll is not null && coll.Value.Element is INamedTypeSymbol elemNt)
        {
            if (elemNt.IsValueType || elemNt.SpecialType == SpecialType.System_String)
            {
                // Collection of values — wrap in ToList/ToArray (caller may want a fresh copy).
                var toCallVal = coll.Value.CollectionKind == "array"
                    ? "global::System.Linq.Enumerable.ToArray(" + srcExpr + ")"
                    : "global::System.Linq.Enumerable.ToList(" + srcExpr + ")";
                sb.Append(srcExpr).Append(" is null ? null! : ").Append(toCallVal);
                return;
            }

            // Element has explicit nested [Map<elem, elem>] → call Map(x, tracker).
            string elemExpr;
            if (NestedMappingResolver.FindNestedMapper(cls, elemNt, elemNt) is not null)
            {
                elemExpr = "x is null ? null! : Map(x, tracker)";
            }
            else if (collected.TryGetValue(elemNt, out var elemInfo) && elemInfo.HasParameterlessCtor)
            {
                elemExpr = "x is null ? null! : __CloneCycleSafe_" + MangleTypeNameForCall(elemNt) + "(x, tracker)";
            }
            else
            {
                // Primary-ctor-only elem; inline construction.
                elemExpr = "x is null ? null! : " + EmitInlinePrimaryCtorClone(elemNt, "x", cls, comp, collected);
            }

            var loop = "(global::System.Linq.Enumerable.Select(" + srcExpr + ", x => " + elemExpr + "))";
            var toCall = coll.Value.CollectionKind == "array"
                ? "global::System.Linq.Enumerable.ToArray" + loop
                : "global::System.Linq.Enumerable.ToList" + loop;
            sb.Append(srcExpr).Append(" is null ? null! : ").Append(toCall);
            return;
        }

        // Object case.
        if (propType is INamedTypeSymbol propNt)
        {
            // Explicit nested [Map<,>] declared in MapperClass.
            if (NestedMappingResolver.FindNestedMapper(cls, propNt, propNt) is not null)
            {
                sb.Append(srcExpr).Append(" is null ? null! : Map(").Append(srcExpr).Append(", tracker)");
                return;
            }

            // Parameterless-ctor reachable type → call its helper.
            if (collected.TryGetValue(propNt, out var info) && info.HasParameterlessCtor)
            {
                sb.Append(srcExpr).Append(" is null ? null! : __CloneCycleSafe_")
                  .Append(MangleTypeNameForCall(propNt))
                  .Append("(").Append(srcExpr).Append(", tracker)");
                return;
            }

            // Primary-ctor-only reachable type → inline construction.
            sb.Append(srcExpr).Append(" is null ? null! : ")
              .Append(EmitInlinePrimaryCtorClone(propNt, srcExpr, cls, comp, collected));
            return;
        }

        // Fallback (shouldn't happen for cleanly-typed graphs).
        sb.Append(srcExpr);
    }
#pragma warning restore MA0051

    /// <summary>
    /// Emits an inline <c>new T(arg1: ..., arg2: ...)</c> expression for a primary-ctor-only type.
    /// Does NOT register with the tracker (atomic construction); ZAMP021 should already have fired
    /// if this type is part of a cycle.
    /// </summary>
    internal static string EmitInlinePrimaryCtorClone(
        INamedTypeSymbol type,
        string srcExpr,
        MapperClass cls,
        Compilation comp,
        Dictionary<INamedTypeSymbol, ReachableTypeInfo> collected)
    {
        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var ctor = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (ctor is null || ctor.Parameters.Length == 0)
        {
            // Shouldn't reach (would have parameterless ctor → other branch). Defensive fallback.
            return srcExpr;
        }

        var srcProps = PropertyMatcher.GetAllPublicProperties(type)
            .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

        var sb = new StringBuilder();
        sb.Append("new ").Append(fqn).Append("(");
        var args = ctor.Parameters.Where(p => srcProps.ContainsKey(p.Name)).ToList();
        for (int i = 0; i < args.Count; i++)
        {
            var p = args[i];
            sb.Append(p.Name).Append(": ");
            // Recurse via EmitPropertyAssignment captured into a local sb.
            var inner = new StringBuilder();
            EmitPropertyAssignment(inner, srcExpr + "." + p.Name, p.Type, cls, comp, collected);
            sb.Append(inner);
            if (i + 1 < args.Count) sb.Append(", ");
        }
        sb.Append(")");
        return sb.ToString();
    }

    /// <summary>
    /// Deterministic type-name mangle for the helper-method suffix. Replaces dots and angle brackets
    /// with underscores so generic / nested types produce a single C# identifier.
    /// Example: <c>Ns.Outer+Inner&lt;T&gt;</c> → <c>Ns_Outer_Inner_T_</c>.
    /// </summary>
    internal static string MangleTypeNameForCall(INamedTypeSymbol type)
    {
        var s = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (s.StartsWith("global::", System.StringComparison.Ordinal)) s = s.Substring(8);
        return s
            .Replace('.', '_')
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace('+', '_')
            .Replace("?", string.Empty);
    }
}
