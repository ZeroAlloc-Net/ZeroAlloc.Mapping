using Microsoft.CodeAnalysis;
using System.Text;

namespace ZeroAlloc.Mapping.Generator;

internal static class TryMapEmitter
{
    public static void EmitTryMapMethod(StringBuilder sb, MappingDecl decl, MatchResult match, MapperClass owningClass, Compilation comp)
    {
        var partialKw = decl.UserPartialMethod is not null ? "partial " : "";
        var resultType = "global::ZeroAlloc.Results.Result<" + decl.DestinationTypeFqn + ", global::ZeroAlloc.Mapping.MappingError>";

        sb.Append("    public static ").Append(partialKw).Append(resultType).Append(" TryMap(")
          .Append(decl.SourceTypeFqn).Append(" src)\n    {\n");
        sb.Append("        if (src is null) return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.source.null\", \"(root)\"));\n");
        sb.Append("        try\n        {\n");

        foreach (var hook in MapEmitter.MatchingHooks(owningClass, isAfter: false))
            sb.Append("            ").Append(hook.MethodName).Append("(src);\n");

        sb.Append("            var __dst = new ").Append(decl.DestinationTypeFqn).Append("(\n");

        var totalArgs = match.Mappings.Count + match.Constants.Count;
        var idx = 0;

        foreach (var m in match.Mappings)
        {
            string expr;
            if (m.IsFlattened)
            {
                var op = MapEmitter.FlatteningOperator(m);
                expr = "src." + m.SourcePropertyName.Replace(".", op);
            }
            else
            {
                var conv = ConversionResolver.Resolve(m.SourceType, m.TargetType, comp);
                expr = ConversionResolver.Apply(conv, "src." + m.SourcePropertyName, m.TargetType);
            }
            sb.Append("                ").Append(m.TargetParamName).Append(": ").Append(expr);
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

        foreach (var hook in MapEmitter.MatchingHooks(owningClass, isAfter: true))
            sb.Append("            ").Append(hook.MethodName).Append("(src, __dst);\n");

        sb.Append("            return ").Append(resultType).Append(".Success(__dst);\n");
        sb.Append("        }\n");
        sb.Append("        catch (global::System.Exception ex)\n");
        sb.Append("        {\n");
        sb.Append("            return ").Append(resultType)
          .Append(".Failure(new global::ZeroAlloc.Mapping.MappingError(\"mapping.constructor.threw\", \"(root)\", ex.Message));\n");
        sb.Append("        }\n");
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
