using Microsoft.CodeAnalysis;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// Emits a static <c>Expression&lt;Func&lt;TSrc, TDst&gt;&gt; Projection</c> property body
/// for the given <see cref="MappingDecl"/>. The expression literal assigns every destination
/// constructor parameter explicitly (via an object-initializer that EF Core's LINQ translator
/// can handle as a <see cref="System.Linq.Expressions.MemberInitExpression"/>). Nested
/// <c>[Map&lt;,&gt;]</c> mappings declared on the same class are auto-inlined transitively
/// because EF Core cannot translate cross-mapper method calls.
/// </summary>
internal static class ProjectionExpressionEmitter
{
    /// <summary>Reserved by the runtime emit; the lambda parameter for the outer-most projection.</summary>
    private const string RootParam = "__src";

    public static void EmitProjection(
        StringBuilder sb,
        MappingDecl decl,
        MatchResult match,
        MapperClass cls,
        Compilation comp,
        INamedTypeSymbol srcType,
        INamedTypeSymbol dstType)
    {
        var srcFqn = decl.SourceTypeFqn;
        var dstFqn = decl.DestinationTypeFqn;

        sb.Append("    /// <summary>LINQ expression-tree projection for use with IQueryable.Select.</summary>\n");
        sb.Append("    public static global::System.Linq.Expressions.Expression<global::System.Func<")
          .Append(srcFqn).Append(", ").Append(dstFqn).Append(">> Projection { get; } = ")
          .Append(RootParam).Append(" => new ").Append(dstFqn).Append("\n    {\n");

        EmitInitializerBody(sb, match, cls, comp, RootParam, indent: "        ");

        sb.Append("    };\n\n");
    }

    /// <summary>
    /// Emits the body of an object initializer: a comma-separated list of
    /// <c>PropName = expr</c> lines. Used both at the top-level Projection
    /// emission and recursively when inlining nested mappings.
    /// </summary>
    private static void EmitInitializerBody(
        StringBuilder sb,
        MatchResult match,
        MapperClass cls,
        Compilation comp,
        string srcAccess,
        string indent)
    {
        var totalArgs = match.Mappings.Count + match.Constants.Count;
        var idx = 0;

        foreach (var m in match.Mappings)
        {
            sb.Append(indent).Append(m.TargetParamName).Append(" = ");
            EmitExpressionFor(sb, m, cls, comp, srcAccess, indent);
            if (++idx < totalArgs) sb.Append(',');
            sb.Append('\n');
        }

        foreach (var c in match.Constants)
        {
            sb.Append(indent).Append(c.TargetParamName).Append(" = ").Append(FormatLiteral(c.Value));
            if (++idx < totalArgs) sb.Append(',');
            sb.Append('\n');
        }
    }

    /// <summary>
    /// Emits the right-hand expression for a single destination assignment. Five cases:
    /// (1) flattened dotted-path member access (<c>src.Inner.Value</c>);
    /// (2) nested same-class <c>[Map&lt;,&gt;]</c> — recursively inlined as <c>new Dst { ... }</c>;
    /// (3) collection of nested mapped element — inlined via <c>Enumerable.Select(...).ToList()</c>
    ///     with an inline lambda body (EF translates this);
    /// (4) standard conversion (identity / cast / single-arg ctor / Parse / Enum.Parse).
    ///     Hooks/culture are EXCLUDED at this point because ZAMP017 should have fired if either
    ///     were set; we still forward the configured culture so the emitted code compiles.
    /// </summary>
    private static void EmitExpressionFor(
        StringBuilder sb,
        PropertyMapping m,
        MapperClass cls,
        Compilation comp,
        string srcAccess,
        string indent)
    {
        if (m.IsFlattened)
        {
            // Expression trees do not support the null-forgiving (!.) or null-conditional (?.)
            // operators directly. EF Core translates plain dotted member access just fine,
            // and the SQL projection handles null propagation server-side. So we collapse
            // both flavours to plain "."-access here.
            sb.Append(srcAccess).Append('.').Append(m.SourcePropertyName);
            return;
        }

        var srcExpr = srcAccess + "." + m.SourcePropertyName;

        // Case 3: collection of nested mapped elements.
        var srcColl = NestedMappingResolver.AsCollection(m.SourceType);
        var dstColl = NestedMappingResolver.AsCollection(m.TargetType);
        if (srcColl is not null && dstColl is not null)
        {
            var nested = NestedMappingResolver.FindNestedMapper(cls, srcColl.Value.Element, dstColl.Value.Element);
            if (nested is not null)
            {
                EmitCollectionInline(sb, m, srcExpr, nested, cls, comp, srcColl.Value.Element, dstColl.Value, indent);
                return;
            }
            // Fallback: collection without nested mapping — passthrough (identity element type).
            sb.Append(srcExpr);
            return;
        }

        // Case 2: nested object via a same-class [Map<,>] — inline transitively.
        var nestedObj = NestedMappingResolver.FindNestedMapper(cls, m.SourceType, m.TargetType);
        if (nestedObj is not null && nestedObj.Kind == MappingKind.Map)
        {
            var nestedSrc = nestedObj.SourceTypeSymbol ?? m.SourceType as INamedTypeSymbol;
            var nestedDst = nestedObj.DestinationTypeSymbol ?? m.TargetType as INamedTypeSymbol;
            if (nestedSrc is not null && nestedDst is not null)
            {
                var nestedMatch = PropertyMatcher.Match(nestedSrc, nestedDst, nestedObj.UserPartialMethod, cls.CaseInsensitive);
                if (nestedMatch is not null)
                {
                    sb.Append("new ").Append(nestedObj.DestinationTypeFqn).Append('\n');
                    sb.Append(indent).Append("{\n");
                    EmitInitializerBody(sb, nestedMatch, cls, comp, srcExpr, indent + "    ");
                    sb.Append(indent).Append('}');
                    return;
                }
            }
            // Fallback to a Map(...) call if anything went wrong; will fail EF translation
            // but at least compiles. ZAMP002/ZAMP001 would already have surfaced upstream.
            sb.Append("Map(").Append(srcExpr).Append(')');
            return;
        }

        // Case 4: standard conversion.
        var conv = ConversionResolver.Resolve(m.SourceType, m.TargetType, comp);
        sb.Append(ConversionResolver.Apply(conv, srcExpr, m.TargetType, cls.Culture));
    }

    /// <summary>
    /// Emits an inline <c>Enumerable.Select(...).ToList()</c> (or <c>.ToArray()</c>)
    /// where the selector lambda is the nested element's expression-tree initializer.
    /// Uses unique element-parameter names per nesting depth (length of the current indent)
    /// so deeply-nested projections do not shadow each other.
    /// </summary>
    private static void EmitCollectionInline(
        StringBuilder sb,
        PropertyMapping m,
        string srcExpr,
        MappingDecl nested,
        MapperClass cls,
        Compilation comp,
        ITypeSymbol srcElem,
        (ITypeSymbol Element, string CollectionKind) dstCollInfo,
        string indent)
    {
        var nestedSrc = nested.SourceTypeSymbol ?? srcElem as INamedTypeSymbol;
        var nestedDst = nested.DestinationTypeSymbol ?? dstCollInfo.Element as INamedTypeSymbol;

        var elemParam = "__e" + (indent.Length / 4);
        var dstElemFqn = nested.DestinationTypeFqn;

        sb.Append("global::System.Linq.Enumerable.To")
          .Append(dstCollInfo.CollectionKind == "array" ? "Array" : "List")
          .Append("(global::System.Linq.Enumerable.Select(")
          .Append(srcExpr).Append(", ").Append(elemParam).Append(" => ");

        if (nestedSrc is not null && nestedDst is not null)
        {
            var nestedMatch = PropertyMatcher.Match(nestedSrc, nestedDst, nested.UserPartialMethod, cls.CaseInsensitive);
            if (nestedMatch is not null)
            {
                sb.Append("new ").Append(dstElemFqn).Append('\n');
                sb.Append(indent).Append("{\n");
                EmitInitializerBody(sb, nestedMatch, cls, comp, elemParam, indent + "    ");
                sb.Append(indent).Append('}');
                sb.Append("))");
                return;
            }
        }

        // Fallback: emit a Map call (won't translate in EF, but compiles).
        sb.Append("Map(").Append(elemParam).Append(")))");
    }

    private static string FormatLiteral(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool b => b ? "true" : "false",
        char c => "'" + c + "'",
        _ => value.ToString() ?? "null",
    };
}
