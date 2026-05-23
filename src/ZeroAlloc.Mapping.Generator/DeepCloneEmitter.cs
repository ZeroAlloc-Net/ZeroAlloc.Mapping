using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// Emits a deep-clone <c>Map</c> method for <c>[Map(DeepClone = true)]</c> declarations.
/// Walks the reachable-type graph at compile time and emits literal per-property clone code
/// for every reference-typed property where no explicit nested <c>[Map&lt;,&gt;]</c> declaration
/// exists in the same MapperClass. Explicit nested mappings always win — they remain the
/// family idiom.
///
/// Behaviour per property type:
/// <list type="bullet">
/// <item>Value type / string — direct assignment (bit-copy).</item>
/// <item>Reference type WITH explicit nested mapping — delegate to <c>Map(...)</c>.</item>
/// <item>Reference type WITHOUT explicit nested mapping — emit literal clone (<c>new T { ... }</c>).</item>
/// <item>Collection of reference type WITHOUT explicit nested mapping — emit
///   <c>new List&lt;T&gt;(...)</c> + foreach + Add of cloned elements.</item>
/// </list>
///
/// Combined with <c>CycleSafe = true</c>: the standard <c>EmitCycleSafeMapPair</c> path handles
/// the combination (the tracker resolves cycles at runtime); ZAMP020 does NOT fire in that case.
/// </summary>
internal static class DeepCloneEmitter
{
    public static void EmitDeepCloneMethod(
        StringBuilder sb,
        MappingDecl decl,
        MapperClass cls,
        Compilation comp,
        INamedTypeSymbol srcType,
        INamedTypeSymbol dstType)
    {
        var srcFqn = decl.SourceTypeFqn;
        var dstFqn = decl.DestinationTypeFqn;

        sb.Append("    public static ").Append(dstFqn).Append(" Map(").Append(srcFqn).Append(" src)\n");
        sb.Append("    {\n");
        sb.Append("        global::System.ArgumentNullException.ThrowIfNull(src);\n");

        EmitCloneBody(sb, "__dst", "src", srcType, dstType, cls, comp, "        ", new HashSet<string>(System.StringComparer.Ordinal));

        sb.Append("        return __dst;\n");
        sb.Append("    }\n");
    }

    /// <summary>
    /// Emits the body that constructs an instance of <paramref name="dstType"/> from
    /// <paramref name="srcAccess"/>, assigning the result to a local named
    /// <paramref name="dstLocal"/>. Recurses into reference-typed properties that have no
    /// explicit nested mapping declaration.
    /// </summary>
    private static void EmitCloneBody(
        StringBuilder sb,
        string dstLocal,
        string srcAccess,
        INamedTypeSymbol srcType,
        INamedTypeSymbol dstType,
        MapperClass cls,
        Compilation comp,
        string indent,
        HashSet<string> visiting)
    {
        var dstFqn = dstType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Try parameterless ctor first (simplest case: mutable POCO).
        var parameterlessCtor = dstType.InstanceConstructors.FirstOrDefault(c =>
            c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

        if (parameterlessCtor is not null)
        {
            sb.Append(indent).Append("var ").Append(dstLocal).Append(" = new ").Append(dstFqn).Append("();\n");

            // Walk every public settable property and assign from source.
            var srcProps = PropertyMatcher.GetAllPublicProperties(srcType)
                .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

            foreach (var dp in PropertyMatcher.GetAllPublicProperties(dstType))
            {
                if (PropertyMatcher.IsObsolete(dp)) continue;
                if (dp.SetMethod is not { DeclaredAccessibility: Accessibility.Public } setter) continue;
                if (setter.IsInitOnly) continue;
                if (!srcProps.TryGetValue(dp.Name, out var sp)) continue;
                if (PropertyMatcher.IsObsolete(sp)) continue;

                sb.Append(indent).Append(dstLocal).Append('.').Append(dp.Name).Append(" = ");
                EmitPropertyExpression(sb, srcAccess + "." + sp.Name, sp.Type, dp.Type, cls, comp, indent, visiting);
                sb.Append(";\n");
            }
            return;
        }

        // Fall back to a primary-ctor / largest-ctor call with named args from src.* properties.
        var ctor = dstType.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();

        if (ctor is not null && ctor.Parameters.Length > 0)
        {
            var srcProps = PropertyMatcher.GetAllPublicProperties(srcType)
                .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

            sb.Append(indent).Append("var ").Append(dstLocal).Append(" = new ").Append(dstFqn).Append("(\n");
            var args = ctor.Parameters.Where(p => srcProps.ContainsKey(p.Name)).ToList();
            for (int i = 0; i < args.Count; i++)
            {
                var p = args[i];
                var sp = srcProps[p.Name];
                sb.Append(indent).Append("    ").Append(p.Name).Append(": ");
                EmitPropertyExpression(sb, srcAccess + "." + sp.Name, sp.Type, p.Type, cls, comp, indent + "    ", visiting);
                if (i + 1 < args.Count) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(indent).Append(");\n");
            return;
        }

        // Shouldn't reach here if IsCloneable was honoured; emit a placeholder default.
        sb.Append(indent).Append("var ").Append(dstLocal).Append(" = default(").Append(dstFqn).Append(")!;\n");
    }

    /// <summary>
    /// Emits the right-hand expression for a single property assignment in a deep-clone body.
    /// Recurses through reference-typed properties without an explicit nested
    /// <c>[Map&lt;,&gt;]</c> declaration.
    /// </summary>
    private static void EmitPropertyExpression(
        StringBuilder sb,
        string srcExpr,
        ITypeSymbol srcType,
        ITypeSymbol dstType,
        MapperClass cls,
        Compilation comp,
        string indent,
        HashSet<string> visiting)
    {
        // Case 1: value type or string — direct assignment / standard conversion.
        if (srcType.IsValueType || srcType.SpecialType == SpecialType.System_String)
        {
            var conv = ConversionResolver.Resolve(srcType, dstType, comp);
            sb.Append(ConversionResolver.Apply(conv, srcExpr, dstType, cls.Culture));
            return;
        }

        // Case 2: explicit nested [Map<,>] in same class — delegate.
        var nested = NestedMappingResolver.FindNestedMapper(cls, srcType, dstType);
        if (nested is not null)
        {
            sb.Append(srcExpr).Append(" is null ? null! : Map(").Append(srcExpr).Append(")");
            return;
        }

        // Case 3: collection. May have explicit nested mapping for its element type, or be a
        // collection of value-type/clonable reference elements.
        var srcColl = NestedMappingResolver.AsCollection(srcType);
        var dstColl = NestedMappingResolver.AsCollection(dstType);
        if (srcColl is not null && dstColl is not null)
        {
            EmitCollectionClone(sb, srcExpr, srcColl.Value, dstColl.Value, cls, comp, indent, visiting);
            return;
        }

        // Case 4: reference type without explicit nested mapping — emit literal clone.
        if (srcType is INamedTypeSymbol srcNt && dstType is INamedTypeSymbol dstNt)
        {
            var srcFqn = srcNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (visiting.Contains(srcFqn))
            {
                // Cycle — ZAMP020 should already have fired (or CycleSafe routed elsewhere).
                // Emit a best-effort fallback to keep the emit compiling.
                sb.Append(srcExpr);
                return;
            }

            var dstFqnInner = dstNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // Inline literal clone via object initializer where possible.
            var parameterlessCtor = dstNt.InstanceConstructors.FirstOrDefault(c =>
                c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);

            visiting.Add(srcFqn);
            try
            {
                if (parameterlessCtor is not null)
                {
                    sb.Append(srcExpr).Append(" is null ? null! : new ").Append(dstFqnInner).Append(" {");
                    var srcProps = PropertyMatcher.GetAllPublicProperties(srcNt)
                        .ToDictionary(p => p.Name, System.StringComparer.Ordinal);
                    var first = true;
                    foreach (var dp in PropertyMatcher.GetAllPublicProperties(dstNt))
                    {
                        if (PropertyMatcher.IsObsolete(dp)) continue;
                        if (dp.SetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                        if (!srcProps.TryGetValue(dp.Name, out var sp)) continue;
                        if (PropertyMatcher.IsObsolete(sp)) continue;

                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(' ').Append(dp.Name).Append(" = ");
                        EmitPropertyExpression(sb, srcExpr + "." + sp.Name, sp.Type, dp.Type, cls, comp, indent, visiting);
                    }
                    sb.Append(" }");
                }
                else
                {
                    var ctor = dstNt.InstanceConstructors
                        .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                        .OrderByDescending(c => c.Parameters.Length)
                        .FirstOrDefault();

                    if (ctor is not null && ctor.Parameters.Length > 0)
                    {
                        var srcProps = PropertyMatcher.GetAllPublicProperties(srcNt)
                            .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

                        sb.Append(srcExpr).Append(" is null ? null! : new ").Append(dstFqnInner).Append('(');
                        var args = ctor.Parameters.Where(p => srcProps.ContainsKey(p.Name)).ToList();
                        for (int i = 0; i < args.Count; i++)
                        {
                            var p = args[i];
                            var sp = srcProps[p.Name];
                            if (i > 0) sb.Append(", ");
                            sb.Append(p.Name).Append(": ");
                            EmitPropertyExpression(sb, srcExpr + "." + sp.Name, sp.Type, p.Type, cls, comp, indent, visiting);
                        }
                        sb.Append(')');
                    }
                    else
                    {
                        // Uncloneable — ZAMP019 should have fired; emit passthrough fallback.
                        sb.Append(srcExpr);
                    }
                }
            }
            finally
            {
                visiting.Remove(srcFqn);
            }
            return;
        }

        // Fallback — should not normally hit if diagnostics are clean.
        sb.Append(srcExpr);
    }

    /// <summary>
    /// Emits a deep-clone for a collection property: <c>new List&lt;T&gt;(src.X.Count)</c>
    /// (or <c>new T[src.X.Length]</c>) followed by a foreach that adds a cloned element.
    /// Element clone uses the same per-property logic — recursing through the type graph
    /// for nested reference elements.
    /// </summary>
    private static void EmitCollectionClone(
        StringBuilder sb,
        string srcExpr,
        (ITypeSymbol Element, string CollectionKind) srcColl,
        (ITypeSymbol Element, string CollectionKind) dstColl,
        MapperClass cls,
        Compilation comp,
        string indent,
        HashSet<string> visiting)
    {
        var srcElem = srcColl.Element;
        var dstElem = dstColl.Element;
        var dstElemFqn = dstElem.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        // Use an inline lambda + Select + ToList/ToArray pattern (single-expression form so
        // it can appear on the right-hand side of an assignment / named ctor arg).
        var elemParam = "__e";

        if (dstColl.CollectionKind == "array")
        {
            sb.Append(srcExpr).Append(" is null ? null! : global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Select(")
              .Append(srcExpr).Append(", ").Append(elemParam).Append(" => ");
            EmitPropertyExpression(sb, elemParam, srcElem, dstElem, cls, comp, indent, visiting);
            sb.Append("))");
        }
        else
        {
            sb.Append(srcExpr).Append(" is null ? null! : global::System.Linq.Enumerable.ToList(global::System.Linq.Enumerable.Select(")
              .Append(srcExpr).Append(", ").Append(elemParam).Append(" => ");
            EmitPropertyExpression(sb, elemParam, srcElem, dstElem, cls, comp, indent, visiting);
            sb.Append("))");
        }
    }

    /// <summary>
    /// Determines if a type is cloneable: it must have either a public parameterless
    /// constructor, or a single largest constructor whose parameter names all match
    /// source properties (records). External sealed types with no public ctor are NOT
    /// cloneable.
    /// </summary>
    public static bool IsCloneable(INamedTypeSymbol type, INamedTypeSymbol srcCandidate)
    {
        // Strings and value types are always cloneable.
        if (type.IsValueType || type.SpecialType == SpecialType.System_String) return true;

        var hasParameterlessCtor = type.InstanceConstructors.Any(c =>
            c.DeclaredAccessibility == Accessibility.Public && c.Parameters.Length == 0);
        if (hasParameterlessCtor)
        {
            // Settable members? At least one public settable property exists or no settable members at all (empty type).
            // Pragmatic: parameterless ctor is enough — caller would emit no assignments for an empty type.
            return true;
        }

        // Look for a primary ctor that can be satisfied entirely from src.* properties.
        var ctor = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault();
        if (ctor is null) return false;
        if (ctor.Parameters.Length == 0) return true;

        var srcProps = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var p in PropertyMatcher.GetAllPublicProperties(srcCandidate))
            srcProps.Add(p.Name);
        foreach (var p in ctor.Parameters)
        {
            if (!srcProps.Contains(p.Name)) return false;
        }
        return true;
    }

    /// <summary>
    /// Walks the reachable reference-type graph from a [Map(DeepClone=true)] decl. Yields
    /// (sourceType, destinationType) pairs encountered, tracking visited destination FQNs
    /// to detect cycles. The walker stops at types covered by explicit nested mappings.
    /// </summary>
    public static IEnumerable<ReachableType> WalkReachableReferenceTypes(
        MappingDecl decl, MapperClass cls, Compilation comp)
    {
        if (decl.SourceTypeSymbol is null || decl.DestinationTypeSymbol is null) yield break;

        var stack = new Stack<(INamedTypeSymbol Src, INamedTypeSymbol Dst, ImmutableList<string> Path)>();
        var visitedKeys = new HashSet<string>(System.StringComparer.Ordinal);

        stack.Push((decl.SourceTypeSymbol, decl.DestinationTypeSymbol, ImmutableList<string>.Empty));
        var rootKey = decl.SourceTypeFqn + "->" + decl.DestinationTypeFqn;
        visitedKeys.Add(rootKey);

        while (stack.Count > 0)
        {
            var (src, dst, path) = stack.Pop();
            var srcFqn = src.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var dstFqn = dst.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            yield return new ReachableType(src, dst, srcFqn, dstFqn, path);

            // Walk the destination shape; for each property pair, recurse if reference type
            // and no explicit nested mapping exists.
            var srcProps = PropertyMatcher.GetAllPublicProperties(src)
                .ToDictionary(p => p.Name, System.StringComparer.Ordinal);

            foreach (var dp in PropertyMatcher.GetAllPublicProperties(dst))
            {
                if (PropertyMatcher.IsObsolete(dp)) continue;
                if (!srcProps.TryGetValue(dp.Name, out var sp)) continue;

                // Value type / string — leaf.
                if (sp.Type.IsValueType || sp.Type.SpecialType == SpecialType.System_String) continue;

                // Explicit nested mapping wins — don't walk. But the deep-clone decl ITSELF
                // is also a member of cls.Mappings; we must not treat a self-recursive
                // property like Node.Self as "explicitly mapped" because that would mask
                // the cycle detection. So skip the lookup when the candidate would be the
                // current decl.
                var directNested = NestedMappingResolver.FindNestedMapper(cls, sp.Type, dp.Type);
                if (directNested is not null && directNested != decl) continue;

                // Collection — recurse on element type if reference.
                var srcColl = NestedMappingResolver.AsCollection(sp.Type);
                var dstColl = NestedMappingResolver.AsCollection(dp.Type);
                if (srcColl is not null && dstColl is not null)
                {
                    var se = srcColl.Value.Element;
                    var de = dstColl.Value.Element;
                    if (se.IsValueType || se.SpecialType == SpecialType.System_String) continue;
                    var elemNested = NestedMappingResolver.FindNestedMapper(cls, se, de);
                    if (elemNested is not null && elemNested != decl) continue;
                    if (se is INamedTypeSymbol seNt && de is INamedTypeSymbol deNt)
                    {
                        var key = se.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "->" +
                                  de.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (path.Contains(key) || key == rootKey)
                        {
                            // Cycle — emit a marker (visited=true means cycle).
                            yield return new ReachableType(seNt, deNt, seNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                                deNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), path.Add(key), IsCycle: true);
                            continue;
                        }
                        if (visitedKeys.Add(key))
                        {
                            stack.Push((seNt, deNt, path.Add(key)));
                        }
                    }
                    continue;
                }

                // Plain reference type — recurse.
                if (sp.Type is INamedTypeSymbol spNt && dp.Type is INamedTypeSymbol dpNt)
                {
                    var key = spNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "->" +
                              dpNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    if (path.Contains(key) || key == rootKey)
                    {
                        yield return new ReachableType(spNt, dpNt,
                            spNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            dpNt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                            path.Add(key), IsCycle: true);
                        continue;
                    }
                    if (visitedKeys.Add(key))
                    {
                        stack.Push((spNt, dpNt, path.Add(key)));
                    }
                }
            }
        }
    }

    internal sealed record ReachableType(
        INamedTypeSymbol Source,
        INamedTypeSymbol Destination,
        string SourceFqn,
        string DestinationFqn,
        ImmutableList<string> Path,
        bool IsCycle = false);
}

// Minimal lightweight immutable list to avoid taking a dependency on System.Collections.Immutable here.
internal sealed class ImmutableList<T>
{
    public static readonly ImmutableList<T> Empty = new(System.Array.Empty<T>());
    private readonly T[] _items;
    private ImmutableList(T[] items) { _items = items; }
    public int Count => _items.Length;
    public bool Contains(T item)
    {
        for (int i = 0; i < _items.Length; i++)
            if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(_items[i], item)) return true;
        return false;
    }
    public ImmutableList<T> Add(T item)
    {
        var arr = new T[_items.Length + 1];
        System.Array.Copy(_items, arr, _items.Length);
        arr[_items.Length] = item;
        return new ImmutableList<T>(arr);
    }
}
