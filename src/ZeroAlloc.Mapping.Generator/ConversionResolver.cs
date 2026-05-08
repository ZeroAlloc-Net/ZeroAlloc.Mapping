using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator;

internal enum ConversionKind
{
    None,
    Identity,
    ImplicitOrExplicitCast,
    SingleArgConstructor,
    Parse,
    EnumParse,
}

internal sealed record Conversion(ConversionKind Kind, IMethodSymbol? Method = null);

internal static class ConversionResolver
{
    public static Conversion Resolve(ITypeSymbol source, ITypeSymbol target, Compilation comp)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
            return new Conversion(ConversionKind.Identity);

        var conv = ((CSharpCompilation)comp).ClassifyConversion(source, target);
        if (conv.IsImplicit && conv.Exists)
            return new Conversion(ConversionKind.ImplicitOrExplicitCast);
        if (conv.IsExplicit && conv.Exists)
            return new Conversion(ConversionKind.ImplicitOrExplicitCast);

        // Enum target via Enum.Parse<TEnum>(string)
        if (target.TypeKind == TypeKind.Enum && source.SpecialType == SpecialType.System_String)
            return new Conversion(ConversionKind.EnumParse);

        // Single-arg public constructor: new TTarget(src)
        if (target is INamedTypeSymbol nt)
        {
            foreach (var c in nt.InstanceConstructors)
            {
                if (c.DeclaredAccessibility != Accessibility.Public) continue;
                if (c.Parameters.Length != 1) continue;
                if (SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, source))
                    return new Conversion(ConversionKind.SingleArgConstructor, c);
                var paramConv = ((CSharpCompilation)comp).ClassifyConversion(source, c.Parameters[0].Type);
                if (paramConv.IsImplicit && paramConv.Exists)
                    return new Conversion(ConversionKind.SingleArgConstructor, c);
            }

            // static TTarget Parse(string, IFormatProvider) preferred; fall back to Parse(string).
            var parses = nt.GetMembers("Parse").OfType<IMethodSymbol>()
                .Where(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
                .Where(m => SymbolEqualityComparer.Default.Equals(m.ReturnType, target))
                .Where(m => m.Parameters.Length >= 1 && m.Parameters[0].Type.SpecialType == SpecialType.System_String)
                .ToList();
            var iFormatProvider = comp.GetTypeByMetadataName("System.IFormatProvider");
            var withFormat = parses.FirstOrDefault(m =>
                m.Parameters.Length == 2 &&
                iFormatProvider is not null &&
                SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, iFormatProvider));
            var oneArg = parses.FirstOrDefault(m => m.Parameters.Length == 1);
            var parse = withFormat ?? oneArg;
            if (parse is not null && source.SpecialType == SpecialType.System_String)
                return new Conversion(ConversionKind.Parse, parse);
        }

        return new Conversion(ConversionKind.None);
    }

    public static string Apply(Conversion conv, string srcExpr, ITypeSymbol target, string? culture = null)
    {
        return conv.Kind switch
        {
            ConversionKind.Identity => srcExpr,
            ConversionKind.ImplicitOrExplicitCast => srcExpr,
            ConversionKind.SingleArgConstructor => $"new {target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({srcExpr})",
            ConversionKind.EnumParse => $"global::System.Enum.Parse<{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({srcExpr})",
            ConversionKind.Parse => HasFormatProvider(conv.Method)
                ? $"{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.Parse({srcExpr}, {CultureExpr(culture)})"
                : $"{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.Parse({srcExpr})",
            _ => srcExpr,
        };
    }

    private static string CultureExpr(string? culture) =>
        culture is null
            ? "global::System.Globalization.CultureInfo.InvariantCulture"
            : "global::System.Globalization.CultureInfo.GetCultureInfo(\""
              + culture.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\")";

    private static bool HasFormatProvider(IMethodSymbol? method)
    {
        if (method is null) return false;
        if (method.Parameters.Length != 2) return false;
        var t = method.Parameters[1].Type;
        return t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimEnd('?')
            == "global::System.IFormatProvider"
            || t.Name == "IFormatProvider" && t.ContainingNamespace?.ToDisplayString() == "System";
    }
}
