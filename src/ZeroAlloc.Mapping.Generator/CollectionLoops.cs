using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// Maps a collection member element by element with a for loop, so the member allocates only
/// its destination collection and elements (#153).
/// </summary>
/// <remarks>
/// A member is mapped inside a single expression, a constructor argument or a property
/// assignment, so the loop lives in a private helper on the mapper class:
/// <c>__MapToList</c> or <c>__MapToArray</c>, overloaded on the static type of the source
/// collection. The element mapping is passed as a <c>static</c> lambda, which the compiler
/// caches in a static field, so the call allocates no delegate. A cycle-safe mapping passes the
/// tracker as an argument instead of capturing it, so no closure is allocated either.
///
/// The helpers are emitted once per mapper class, and only for the destination kinds and
/// source shapes the class uses. A LINQ expression-tree projection does not use them: a query
/// provider cannot translate a call to a private method.
/// </remarks>
internal sealed class CollectionLoops
{
    private const string TrackerType = "global::System.Collections.Generic.IDictionary<object, object>";
    private const string ListType = "global::System.Collections.Generic.List";

    private static readonly SymbolDisplayFormat NullableQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>The source shapes, in emit order, each with the parameter type it binds.</summary>
    private static readonly (string Shape, string ParameterType)[] Shapes =
    {
        ("List", ListType + "<TSrc>"),
        ("Array", "TSrc[]"),
        ("IList", "global::System.Collections.Generic.IList<TSrc>"),
        ("IReadOnlyList", "global::System.Collections.Generic.IReadOnlyList<TSrc>"),
        ("IEnumerable", "global::System.Collections.Generic.IEnumerable<TSrc>"),
    };

    private readonly HashSet<string> _used = new(System.StringComparer.Ordinal);

    /// <summary>
    /// The expression that maps <paramref name="srcExpr"/> into a new <paramref name="dstKind"/>
    /// collection, mapping each element with <paramref name="elementExpr"/> written in terms of
    /// <paramref name="elementParam"/>. With <paramref name="withTracker"/>, the element
    /// expression may also use <c>tracker</c>. The source collection must not be null; a null
    /// throws <see cref="System.ArgumentNullException"/>, as <c>Enumerable.Select</c> did.
    /// </summary>
    public string Map(
        string srcExpr,
        ITypeSymbol srcCollection,
        ITypeSymbol srcElement,
        ITypeSymbol dstElement,
        string dstKind,
        string elementParam,
        string elementExpr,
        bool withTracker)
    {
        var helper = dstKind == "array" ? "__MapToArray" : "__MapToList";
        _used.Add(Key(helper, Shape(srcCollection), withTracker));

        var sb = new StringBuilder();
        sb.Append(helper).Append('<')
          .Append(srcElement.ToDisplayString(NullableQualifiedFormat)).Append(", ")
          .Append(dstElement.ToDisplayString(NullableQualifiedFormat)).Append(">(")
          .Append(srcExpr).Append(", ");
        if (withTracker)
        {
            sb.Append("tracker, static (").Append(elementParam).Append(", tracker) => ");
        }
        else
        {
            sb.Append("static ").Append(elementParam).Append(" => ");
        }
        sb.Append(elementExpr).Append(')');
        return sb.ToString();
    }

    /// <summary>Emits the helpers <see cref="Map"/> has called for, in a fixed order.</summary>
    public void EmitHelpers(StringBuilder sb)
    {
        foreach (var helper in new[] { "__MapToList", "__MapToArray" })
        {
            foreach (var withTracker in new[] { false, true })
            {
                foreach (var (shape, parameterType) in Shapes)
                {
                    if (!_used.Contains(Key(helper, shape, withTracker))) continue;
                    EmitHelper(sb, helper == "__MapToArray", shape, parameterType, withTracker);
                }
            }
        }
    }

    private static string Key(string helper, string shape, bool withTracker) =>
        helper + "|" + shape + "|" + (withTracker ? "tracker" : "plain");

    /// <summary>The overload a source collection of static type <paramref name="type"/> binds.</summary>
    private static string Shape(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol) return "Array";
        if (type is not INamedTypeSymbol named) return "IEnumerable";
        return named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) switch
        {
            "global::System.Collections.Generic.List<T>" => "List",
            "global::System.Collections.Generic.IList<T>" => "IList",
            "global::System.Collections.Generic.IReadOnlyList<T>" => "IReadOnlyList",
            _ => "IEnumerable",
        };
    }

    private static void EmitHelper(StringBuilder sb, bool toArray, string shape, string parameterType, bool withTracker)
    {
        var dstType = toArray ? "TDst[]" : ListType + "<TDst>";
        var mapType = withTracker
            ? "global::System.Func<TSrc, " + TrackerType + ", TDst>"
            : "global::System.Func<TSrc, TDst>";
        var mapArgs = withTracker ? ", tracker)" : ")";

        sb.Append("    private static ").Append(dstType).Append(' ')
          .Append(toArray ? "__MapToArray" : "__MapToList").Append("<TSrc, TDst>(")
          .Append(parameterType).Append(" src, ");
        if (withTracker) sb.Append(TrackerType).Append(" tracker, ");
        sb.Append(mapType).Append(" map)\n    {\n");
        sb.Append("        global::System.ArgumentNullException.ThrowIfNull(src);\n");

        if (shape == "IEnumerable")
        {
            // A list behind the interface is walked by index, without boxing an enumerator.
            sb.Append("        if (src is global::System.Collections.Generic.IList<TSrc> __list)\n        {\n");
            EmitIndexLoop(sb, "            ", toArray, "__list", "Count", mapArgs, "__fromList");
            sb.Append("        }\n");
            EmitEnumerationLoop(sb, toArray, mapArgs);
        }
        else
        {
            EmitIndexLoop(sb, "        ", toArray, "src", shape == "Array" ? "Length" : "Count", mapArgs, "__dst");
        }

        sb.Append("    }\n");
    }

    private static void EmitIndexLoop(
        StringBuilder sb, string indent, bool toArray, string src, string count, string mapArgs, string dst)
    {
        sb.Append(indent).Append("var ").Append(dst).Append(" = new ")
          .Append(toArray ? "TDst[" + src + "." + count + "]" : ListType + "<TDst>(" + src + "." + count + ")")
          .Append(";\n");
        sb.Append(indent).Append("for (int i = 0; i < ").Append(src).Append('.').Append(count).Append("; i++)\n");
        sb.Append(indent).Append("    ")
          .Append(dst).Append(toArray ? "[i] = " : ".Add(")
          .Append("map(").Append(src).Append("[i]").Append(mapArgs)
          .Append(toArray ? ";\n" : ");\n");
        sb.Append(indent).Append("return ").Append(dst).Append(";\n");
    }

    /// <summary>
    /// A collection that can only be enumerated is sized up front when it knows its count. An
    /// array destination of one that does not is filled through a list, which is then copied.
    /// </summary>
    private static void EmitEnumerationLoop(StringBuilder sb, bool toArray, string mapArgs)
    {
        if (toArray)
        {
            sb.Append("        if (global::System.Linq.Enumerable.TryGetNonEnumeratedCount(src, out var __count))\n        {\n");
            sb.Append("            var __array = new TDst[__count];\n");
            sb.Append("            var i = 0;\n");
            sb.Append("            foreach (var __e in src)\n");
            sb.Append("                __array[i++] = map(__e").Append(mapArgs).Append(";\n");
            sb.Append("            return __array;\n");
            sb.Append("        }\n");
            sb.Append("        var __dst = new ").Append(ListType).Append("<TDst>();\n");
        }
        else
        {
            sb.Append("        var __dst = global::System.Linq.Enumerable.TryGetNonEnumeratedCount(src, out var __capacity)\n");
            sb.Append("            ? new ").Append(ListType).Append("<TDst>(__capacity)\n");
            sb.Append("            : new ").Append(ListType).Append("<TDst>();\n");
        }
        sb.Append("        foreach (var __e in src)\n");
        sb.Append("            __dst.Add(map(__e").Append(mapArgs).Append(");\n");
        sb.Append("        return ").Append(toArray ? "__dst.ToArray()" : "__dst").Append(";\n");
    }
}
