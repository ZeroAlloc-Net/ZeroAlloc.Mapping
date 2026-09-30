using Microsoft.CodeAnalysis;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

internal static class TryMapEmitter
{
    public static void EmitTryMapMethod(StringBuilder sb, MappingDecl decl, MatchResult match, MapperClass owningClass, Compilation comp, ITypeSymbol srcType, ITypeSymbol dstType)
    {
        var partialKw = decl.UserPartialMethod is not null ? "partial " : "";
        var resultType = "global::ZeroAlloc.Results.Result<" + decl.DestinationTypeFqn + ", global::ZeroAlloc.Mapping.MappingError>";

        sb.Append("    public static ").Append(partialKw).Append(resultType).Append(" TryMap(")
          .Append(decl.SourceTypeFqn).Append(" src)\n    {\n");
        // A value-type source cannot be null, and `src is null` does not compile for one.
        if (CanBeNull(srcType))
        {
            sb.Append("        if (src is null) return ").Append(resultType)
              .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", \"(root)\"));\n");
        }
        sb.Append("        try\n        {\n");

        foreach (var hook in MapEmitter.MatchingHooks(owningClass, isAfter: false, srcType, dstType, comp))
        {
            sb.Append("            try { ").Append(hook.MethodName).Append("(src); }\n");
            sb.Append("            catch (global::System.Exception hookEx) { return ").Append(resultType)
              .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.hook.threw\", \"(root)\", hookEx.Message)); }\n");
        }

        // A member without a conversion path is reported as ZAMP002 and left out, rather than
        // emitted as an argument that cannot compile.
        var mappings = match.Mappings
            .Where(m => !ConversionResolver.HasNoConversionPath(m, owningClass, comp, out _))
            .ToList();

        // A member that chains to a nested mapping is mapped first, into a local, so that a
        // nested [TryMap] failure can return with the member's path.
        var args = new System.Collections.Generic.List<string>(mappings.Count);
        for (var i = 0; i < mappings.Count; i++)
        {
            args.Add(EmitMember(sb, mappings[i], i, resultType, owningClass, comp));
        }

        sb.Append("            var __dst = new ").Append(decl.DestinationTypeFqn).Append("(\n");

        var totalArgs = mappings.Count + match.Constants.Count;
        var idx = 0;

        for (var i = 0; i < mappings.Count; i++)
        {
            sb.Append("                ").Append(mappings[i].TargetParamName).Append(": ").Append(args[i]);
            if (++idx < totalArgs) sb.Append(',');
            sb.Append('\n');
        }

        foreach (var c in match.Constants)
        {
            sb.Append("                ").Append(c.TargetParamName).Append(": ").Append(FormatLiteral(c.Value));
            if (++idx < totalArgs) sb.Append(',');
            sb.Append('\n');
        }

        sb.Append("            );\n");

        foreach (var hook in MapEmitter.MatchingHooks(owningClass, isAfter: true, srcType, dstType, comp))
        {
            sb.Append("            try { ").Append(hook.MethodName).Append("(src, __dst); }\n");
            sb.Append("            catch (global::System.Exception hookEx) { return ").Append(resultType)
              .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.hook.threw\", \"(root)\", hookEx.Message)); }\n");
        }

        sb.Append("            return ").Append(resultType).Append(".Success(__dst);\n");
        sb.Append("        }\n");
        sb.Append("        catch (global::System.Exception ex)\n");
        sb.Append("        {\n");
        sb.Append("            return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.constructor.threw\", \"(root)\", ex.Message));\n");
        sb.Append("        }\n");
        sb.Append("    }\n");
    }

    /// <summary>
    /// Emits whatever one member needs before the constructor call, and returns the argument
    /// expression for it.
    /// </summary>
    /// <remarks>
    /// A member or collection element whose types have a nested mapping in the same class chains
    /// to it, preferring a nested [TryMap]. A nested [TryMap] failure returns at once with the
    /// member's name prefixed to its path; failed collection elements are gathered as children of
    /// one <c>mapping.collection.elements_failed</c> error. A null member maps to null when the
    /// destination member is nullable, and fails with <c>mapping.source.null</c> at its path
    /// otherwise. A nested [Map] cannot fail this way; if it throws, the enclosing catch reports it.
    /// </remarks>
    private static string EmitMember(
        StringBuilder sb, PropertyMapping m, int index, string resultType, MapperClass owningClass, Compilation comp)
    {
        if (m.IsFlattened)
        {
            return "src." + m.SourcePropertyName.Replace(".", MapEmitter.FlatteningOperator(m));
        }

        var srcExpr = "src." + m.SourcePropertyName;
        var local = "__m" + index;

        var srcColl = NestedMappingResolver.AsCollection(m.SourceType);
        var dstColl = NestedMappingResolver.AsCollection(m.TargetType);
        if (srcColl is { } sc && dstColl is { } dc &&
            NestedMappingResolver.FindNestedMapper(owningClass, sc.Element, dc.Element, MappingKind.TryMap) is { } elementMapper)
        {
            return EmitCollectionMember(sb, m, local, srcExpr, sc.Element, dc, elementMapper.Kind, resultType);
        }

        if (NestedMappingResolver.FindNestedMapper(owningClass, m.SourceType, m.TargetType, MappingKind.TryMap) is { } nested)
        {
            EmitObjectMember(sb, m, local, srcExpr, nested.Kind, resultType);
            return local;
        }

        var conv = ConversionResolver.Resolve(m.SourceType, m.TargetType, comp);
        return ConversionResolver.Apply(conv, srcExpr, m.TargetType, owningClass.Culture);
    }

    private static void EmitObjectMember(
        StringBuilder sb, PropertyMapping m, string local, string srcExpr, MappingKind nestedKind, string resultType)
    {
        const string indent = "            ";
        var path = Literal(m.SourcePropertyName);
        if (CanBeNull(m.SourceType))
        {
            if (IsNullable(m.TargetType))
            {
                sb.Append(indent).Append(TypeName(m.TargetType)).Append(' ').Append(local).Append(" = null;\n");
                sb.Append(indent).Append("if (").Append(srcExpr).Append(" is not null)\n");
                sb.Append(indent).Append("{\n");
                EmitNestedCall(sb, indent + "    ", local, declare: false, srcExpr, nestedKind, resultType, path);
                sb.Append(indent).Append("}\n");
                return;
            }

            sb.Append(indent).Append("if (").Append(srcExpr).Append(" is null) return ").Append(resultType)
              .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", ").Append(path).Append("));\n");
        }
        EmitNestedCall(sb, indent, local, declare: true, srcExpr, nestedKind, resultType, path);
    }

    /// <summary>
    /// Maps one value with the nested mapping into <paramref name="local"/>. A failed nested
    /// [TryMap] returns its error with <paramref name="pathExpr"/> prefixed.
    /// </summary>
    private static void EmitNestedCall(
        StringBuilder sb, string indent, string local, bool declare, string valueExpr,
        MappingKind nestedKind, string resultType, string pathExpr)
    {
        var target = (declare ? "var " : "") + local;
        if (nestedKind == MappingKind.Map)
        {
            sb.Append(indent).Append(target).Append(" = Map(").Append(valueExpr).Append(");\n");
            return;
        }

        var r = local + "r";
        sb.Append(indent).Append("var ").Append(r).Append(" = TryMap(").Append(valueExpr).Append(");\n");
        sb.Append(indent).Append("if (!").Append(r).Append(".IsSuccess) return ").Append(resultType)
          .Append(".Failure(").Append(PrefixedError(r, pathExpr)).Append(");\n");
        sb.Append(indent).Append(target).Append(" = ").Append(r).Append(".Value;\n");
    }

    /// <summary>
    /// Maps a collection member element by element into <paramref name="local"/> and returns
    /// the argument expression. Every failed element is kept, and the member fails with all of
    /// them as children. Nothing is allocated for failures unless one happens.
    /// </summary>
    /// <remarks>
    /// An indexable source is walked by index and sizes the result up front: an array
    /// destination is filled in place, any other gets a list with that capacity. A source that
    /// can only be enumerated is walked with foreach into a list, copied to an array when the
    /// destination is one.
    /// </remarks>
    private static string EmitCollectionMember(
        StringBuilder sb, PropertyMapping m, string local, string srcExpr, ITypeSymbol srcElement,
        (ITypeSymbol Element, string CollectionKind) dstColl, MappingKind nestedKind, string resultType)
    {
        const string indent = "            ";
        var path = Literal(m.SourcePropertyName);
        var elementType = TypeName(dstColl.Element);
        var countMember = CountMember(m.SourceType);
        var count = countMember is null ? null : srcExpr + "." + countMember;
        var toArray = dstColl.CollectionKind == "array";
        var fillArray = toArray && count is not null;
        var storageType = fillArray ? elementType + "[]" : "global::System.Collections.Generic.List<" + elementType + ">";
        var create = fillArray
            ? "new " + elementType + "[" + count + "]"
            : "new global::System.Collections.Generic.List<" + elementType + ">(" + count + ")";
        var passNullThrough = CanBeNull(m.SourceType) && IsNullable(m.TargetType);
        var inner = indent;

        if (passNullThrough)
        {
            sb.Append(indent).Append(storageType).Append("? ").Append(local).Append(" = null;\n");
            sb.Append(indent).Append("if (").Append(srcExpr).Append(" is not null)\n");
            sb.Append(indent).Append("{\n");
            inner = indent + "    ";
            sb.Append(inner).Append(local).Append(" = ").Append(create).Append(";\n");
        }
        else
        {
            if (CanBeNull(m.SourceType))
            {
                sb.Append(indent).Append("if (").Append(srcExpr).Append(" is null) return ").Append(resultType)
                  .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", ").Append(path).Append("));\n");
            }
            sb.Append(indent).Append("var ").Append(local).Append(" = ").Append(create).Append(";\n");
        }

        var failures = local + "f";
        var i = local + "i";
        var e = local + "e";
        var body = inner + "    ";
        var elementPath = path + " + \"[\" + " + i + " + \"]\"";
        string Store(string value) => fillArray
            ? local + "[" + i + "] = " + value + ";"
            : local + ".Add(" + value + ");";

        sb.Append(inner).Append("global::System.Collections.Generic.List<global::ZeroAlloc.Mapping.MappingError>? ")
          .Append(failures).Append(" = null;\n");
        sb.Append(inner).Append("var ").Append(i).Append(" = 0;\n");
        if (count is null)
        {
            sb.Append(inner).Append("foreach (var ").Append(e).Append(" in ").Append(srcExpr).Append(")\n");
            sb.Append(inner).Append("{\n");
        }
        else
        {
            sb.Append(inner).Append("for (; ").Append(i).Append(" < ").Append(count).Append("; ").Append(i).Append("++)\n");
            sb.Append(inner).Append("{\n");
            sb.Append(body).Append("var ").Append(e).Append(" = ").Append(srcExpr).Append('[').Append(i).Append("];\n");
        }

        var elementBody = body;
        if (CanBeNull(srcElement))
        {
            sb.Append(body).Append("if (").Append(e).Append(" is null)\n");
            sb.Append(body).Append("{\n");
            if (IsNullable(dstColl.Element))
            {
                sb.Append(body).Append("    ").Append(Store("null")).Append('\n');
            }
            else
            {
                sb.Append(body).Append("    (").Append(failures)
                  .Append(" ??= new global::System.Collections.Generic.List<global::ZeroAlloc.Mapping.MappingError>())")
                  .Append(".Add(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", ").Append(elementPath).Append("));\n");
            }
            sb.Append(body).Append("}\n");
            sb.Append(body).Append("else\n");
            sb.Append(body).Append("{\n");
            elementBody = body + "    ";
        }

        if (nestedKind == MappingKind.Map)
        {
            sb.Append(elementBody).Append(Store("Map(" + e + ")")).Append('\n');
        }
        else
        {
            var r = local + "r";
            sb.Append(elementBody).Append("var ").Append(r).Append(" = TryMap(").Append(e).Append(");\n");
            sb.Append(elementBody).Append("if (").Append(r).Append(".IsSuccess) ").Append(Store(r + ".Value")).Append('\n');
            sb.Append(elementBody).Append("else (").Append(failures)
              .Append(" ??= new global::System.Collections.Generic.List<global::ZeroAlloc.Mapping.MappingError>())")
              .Append(".Add(").Append(PrefixedError(r, elementPath)).Append(");\n");
        }

        if (elementBody != body) sb.Append(body).Append("}\n");
        if (count is null) sb.Append(body).Append(i).Append("++;\n");
        sb.Append(inner).Append("}\n");
        sb.Append(inner).Append("if (").Append(failures).Append(" is not null) return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.collection.elements_failed\", ").Append(path)
          .Append(", ").Append(failures).Append(".Count + \" of \" + ").Append(i).Append(" + \" elements failed\", ")
          .Append(failures).Append("));\n");

        if (inner != indent) sb.Append(indent).Append("}\n");

        if (!toArray || fillArray) return local;
        return passNullThrough ? local + "?.ToArray()" : local + ".ToArray()";
    }

    /// <summary>
    /// The member that counts an indexable collection: <c>Length</c> for an array, <c>Count</c>
    /// for a list; null for a collection that can only be enumerated.
    /// </summary>
    private static string? CountMember(ITypeSymbol collection)
    {
        if (collection is IArrayTypeSymbol) return "Length";
        return collection is INamedTypeSymbol named &&
            named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) is
                "global::System.Collections.Generic.List<T>" or
                "global::System.Collections.Generic.IList<T>" or
                "global::System.Collections.Generic.IReadOnlyList<T>"
            ? "Count"
            : null;
    }

    /// <summary>
    /// The error of the failed result <paramref name="r"/> with <paramref name="pathExpr"/>
    /// prefixed to its path, the way the collection overloads prefix an element index.
    /// </summary>
    private static string PrefixedError(string r, string pathExpr) =>
        "new global::ZeroAlloc.Mapping.MappingError(" + r + ".Error.Code, " + pathExpr + " + (" + r +
        ".Error.PropertyPath == \"(root)\" ? \"\" : \".\" + " + r + ".Error.PropertyPath), " + r +
        ".Error.Reason, " + r + ".Error.Children)";

    /// <summary>A reference type or a <see cref="System.Nullable{T}"/>.</summary>
    private static bool CanBeNull(ITypeSymbol type) =>
        !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static readonly SymbolDisplayFormat NullableQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static string TypeName(ITypeSymbol type) => type.ToDisplayString(NullableQualifiedFormat);

    private static string Literal(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static void EmitPolymorphicTryDispatcher(StringBuilder sb, PolymorphicDecl poly, System.Collections.Generic.List<MappingDecl> cases)
    {
        var resultType = "global::ZeroAlloc.Results.Result<" + poly.BaseDestinationTypeFqn + ", global::ZeroAlloc.Mapping.MappingError>";

        sb.Append("    public static ").Append(resultType).Append(" TryMap(")
          .Append(poly.BaseTypeFqn).Append(" src)\n    {\n");
        sb.Append("        if (src is null) return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", \"(root)\"));\n");

        for (int i = 0; i < cases.Count; i++)
        {
            var c = cases[i];
            // Inline conversion: Result<TDerivedDto, E> -> Result<TBaseDto, E> via explicit Success/Failure
            // (Result<,> is invariant in T, so we cannot return the derived Result directly).
            sb.Append("        if (src is ").Append(c.SourceTypeFqn).Append(" __").Append(i).Append(")\n        {\n");
            sb.Append("            var __r").Append(i).Append(" = TryMap(__").Append(i).Append(");\n");
            sb.Append("            return __r").Append(i).Append(".IsSuccess\n");
            sb.Append("                ? ").Append(resultType).Append(".Success(__r").Append(i).Append(".Value)\n");
            sb.Append("                : ").Append(resultType).Append(".Failure(__r").Append(i).Append(".Error);\n");
            sb.Append("        }\n");
        }

        sb.Append("        return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.polymorphic.unhandled_type\", \"(root)\", \"runtime type \" + src.GetType().FullName + \" has no declared [TryMap]\"));\n");
        sb.Append("    }\n");
    }

    internal static void EmitTryMapCollectionOverloads(StringBuilder sb, string srcFqn, string dstFqn)
    {
        var resultListType = "global::ZeroAlloc.Results.Result<global::System.Collections.Generic.List<" + dstFqn + ">, global::ZeroAlloc.Mapping.MappingError>";
        var resultArrayType = "global::ZeroAlloc.Results.Result<" + dstFqn + "[], global::ZeroAlloc.Mapping.MappingError>";
        var resultEnumType = "global::ZeroAlloc.Results.Result<global::System.Collections.Generic.IEnumerable<" + dstFqn + ">, global::ZeroAlloc.Mapping.MappingError>";
        var resultRoListType = "global::ZeroAlloc.Results.Result<global::System.Collections.Generic.IReadOnlyList<" + dstFqn + ">, global::ZeroAlloc.Mapping.MappingError>";

        EmitTryMapCollectionMaterialized(sb, resultListType, srcFqn, dstFqn,
            srcCollection: "global::System.Collections.Generic.List<" + srcFqn + ">",
            countMember: "Count",
            builderInit: "var __dst = new global::System.Collections.Generic.List<" + dstFqn + ">(src.Count);",
            appendItem: "__dst.Add(__r.Value);");

        EmitTryMapCollectionMaterialized(sb, resultArrayType, srcFqn, dstFqn,
            srcCollection: srcFqn + "[]",
            countMember: "Length",
            builderInit: "var __dst = new " + dstFqn + "[src.Length];",
            appendItem: "__dst[i] = __r.Value;");

        // IEnumerable<TSrc> — eager materialisation, returned as IEnumerable<TDst>.
        EmitTryMapCollectionMaterialized(sb, resultEnumType, srcFqn, dstFqn,
            srcCollection: "global::System.Collections.Generic.IEnumerable<" + srcFqn + ">",
            countMember: null,
            builderInit: "var __dst = new global::System.Collections.Generic.List<" + dstFqn + ">();",
            appendItem: "__dst.Add(__r.Value);");

        EmitTryMapCollectionMaterialized(sb, resultRoListType, srcFqn, dstFqn,
            srcCollection: "global::System.Collections.Generic.IReadOnlyList<" + srcFqn + ">",
            countMember: "Count",
            builderInit: "var __dst = new " + dstFqn + "[src.Count];",
            appendItem: "__dst[i] = __r.Value;");
    }

    private static void EmitTryMapCollectionMaterialized(
        StringBuilder sb, string resultType, string srcFqn, string dstFqn,
        string srcCollection, string? countMember, string builderInit, string appendItem)
    {
        sb.Append("    public static ").Append(resultType).Append(" TryMap(").Append(srcCollection).Append(" src)\n    {\n");
        sb.Append("        if (src is null) return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", \"(root)\"));\n");
        sb.Append("        ").Append(builderInit).Append("\n");
        sb.Append("        var __failures = new global::System.Collections.Generic.List<global::ZeroAlloc.Mapping.MappingError>();\n");

        if (countMember is not null)
        {
            sb.Append("        for (int i = 0; i < src.").Append(countMember).Append("; i++)\n        {\n");
            sb.Append("            var __r = TryMap(src[i]);\n");
            sb.Append("            if (__r.IsSuccess) ").Append(appendItem).Append("\n");
            sb.Append("            else __failures.Add(new global::ZeroAlloc.Mapping.MappingError(__r.Error.Code, \"[\" + i + \"]\" + (__r.Error.PropertyPath == \"(root)\" ? \"\" : \".\" + __r.Error.PropertyPath), __r.Error.Reason, __r.Error.Children));\n");
            sb.Append("        }\n");
        }
        else
        {
            sb.Append("        int __i = 0;\n");
            sb.Append("        foreach (var __item in src)\n        {\n");
            sb.Append("            var __r = TryMap(__item);\n");
            sb.Append("            if (__r.IsSuccess) ").Append(appendItem).Append("\n");
            sb.Append("            else __failures.Add(new global::ZeroAlloc.Mapping.MappingError(__r.Error.Code, \"[\" + __i + \"]\" + (__r.Error.PropertyPath == \"(root)\" ? \"\" : \".\" + __r.Error.PropertyPath), __r.Error.Reason, __r.Error.Children));\n");
            sb.Append("            __i++;\n");
            sb.Append("        }\n");
        }

        sb.Append("        if (__failures.Count > 0) return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.collection.elements_failed\", \"(root)\", __failures.Count + \" of \" + ");
        sb.Append(countMember is not null ? "src." + countMember : "__i");
        sb.Append(" + \" elements failed\", __failures));\n");
        sb.Append("        return ").Append(resultType).Append(".Success(__dst);\n");
        sb.Append("    }\n");
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
