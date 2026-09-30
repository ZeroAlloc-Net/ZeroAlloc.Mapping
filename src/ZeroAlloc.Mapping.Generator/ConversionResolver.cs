using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Mapping.Generator;

internal enum ConversionKind
{
    None,
    Identity,
    ImplicitCast,
    /// <summary>
    /// Only an explicit conversion exists. It can throw or lose data, so it is not applied and
    /// the member is reported as ZAMP002 instead of emitted.
    /// </summary>
    Explicit,
    SingleArgConstructor,
    Parse,
    EnumParse,
    /// <summary>
    /// A collection whose elements convert by identity or implicitly, copied element by element
    /// into the destination collection kind.
    /// </summary>
    CollectionElements,
}

/// <summary>
/// How a member converts. <see cref="Collection"/> is set when both sides are collections: for
/// <see cref="ConversionKind.CollectionElements"/> it says how, and for a member without a path
/// it names the element types that do not convert.
/// </summary>
internal sealed record Conversion(ConversionKind Kind, IMethodSymbol? Method = null, CollectionConversion? Collection = null);

/// <summary>
/// The element conversion of a collection member. <see cref="NullPassThrough"/> is set when a
/// null source collection maps to a null destination.
/// </summary>
internal sealed record CollectionConversion(
    ITypeSymbol SourceElement,
    ITypeSymbol TargetElement,
    ConversionKind ElementKind,
    string TargetCollectionKind,
    bool NullPassThrough);

internal static class ConversionResolver
{
    public static Conversion Resolve(ITypeSymbol source, ITypeSymbol target, Compilation comp)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
            return new Conversion(ConversionKind.Identity);

        var conv = ((CSharpCompilation)comp).ClassifyConversion(source, target);
        if (conv.IsImplicit && conv.Exists)
            return new Conversion(ConversionKind.ImplicitCast);
        // An explicit conversion is not applied, but a constructor or Parse below may still be.
        var explicitOnly = conv.IsExplicit && conv.Exists;

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

        // Two collections whose elements convert implicitly are copied element by element.
        // When the elements do not, the conversion names them, for ZAMP002.
        if (NestedMappingResolver.AsCollection(source) is { } sc && NestedMappingResolver.AsCollection(target) is { } tc)
        {
            var element = Resolve(sc.Element, tc.Element, comp).Kind;
            var converts = element is ConversionKind.Identity or ConversionKind.ImplicitCast;
            var collection = new CollectionConversion(
                sc.Element,
                tc.Element,
                converts || element == ConversionKind.Explicit ? element : ConversionKind.None,
                tc.CollectionKind,
                NullPassThrough: CanBeNull(source) && IsNullable(target));
            return new Conversion(converts ? ConversionKind.CollectionElements : ConversionKind.None, Collection: collection);
        }

        return new Conversion(explicitOnly ? ConversionKind.Explicit : ConversionKind.None);
    }

    private static bool CanBeNull(ITypeSymbol type) =>
        !type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated ||
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    /// <summary>
    /// True when a matched member has no conversion the generator applies: no implicit
    /// conversion, constructor or Parse, no nested mapper, and, for two collections, neither an
    /// implicit element conversion nor a nested mapper for the elements. Such a member is
    /// reported as ZAMP002 and left out of the generated mapping.
    /// </summary>
    public static bool HasNoConversionPath(PropertyMapping m, MapperClass cls, Compilation comp, out Conversion conversion)
    {
        conversion = Resolve(m.SourceType, m.TargetType, comp);
        if (conversion.Kind is not (ConversionKind.None or ConversionKind.Explicit)) return false;
        if (NestedMappingResolver.FindNestedMapper(cls, m.SourceType, m.TargetType) is not null) return false;
        return conversion.Collection is not { } c ||
            NestedMappingResolver.FindNestedMapper(cls, c.SourceElement, c.TargetElement) is null;
    }

    public static string Apply(Conversion conv, string srcExpr, ITypeSymbol target, string? culture = null)
    {
        return conv.Kind switch
        {
            ConversionKind.Identity => srcExpr,
            ConversionKind.ImplicitCast => srcExpr,
            ConversionKind.SingleArgConstructor => $"new {target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}({srcExpr})",
            ConversionKind.EnumParse => $"global::System.Enum.Parse<{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}>({srcExpr})",
            ConversionKind.Parse => HasFormatProvider(conv.Method)
                ? $"{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.Parse({srcExpr}, {CultureExpr(culture)})"
                : $"{target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.Parse({srcExpr})",
            ConversionKind.CollectionElements => ApplyCollection(conv.Collection!, srcExpr),
            _ => srcExpr,
        };
    }

    private static readonly SymbolDisplayFormat NullableQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Copies a collection into the destination kind, casting each element when the element
    /// types differ. The cast only spells out an implicit conversion. The lambda is not static,
    /// so the same expression works inside a projection's expression tree.
    /// </summary>
    private static string ApplyCollection(CollectionConversion c, string srcExpr)
    {
        var elements = c.ElementKind == ConversionKind.Identity
            ? srcExpr
            : "global::System.Linq.Enumerable.Select(" + srcExpr + ", __x => (" +
              c.TargetElement.ToDisplayString(NullableQualifiedFormat) + ")__x)";
        var copy = (c.TargetCollectionKind == "array"
            ? "global::System.Linq.Enumerable.ToArray("
            : "global::System.Linq.Enumerable.ToList(") + elements + ")";
        return c.NullPassThrough ? "(" + srcExpr + " is null ? null : " + copy + ")" : copy;
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
