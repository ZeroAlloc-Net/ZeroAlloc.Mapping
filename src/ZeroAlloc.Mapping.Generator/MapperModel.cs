namespace ZeroAlloc.Mapping.Generator;

internal enum MappingKind
{
    Map,
    TryMap
}

internal sealed record HookMethod(
    string MethodName,
    Microsoft.CodeAnalysis.ITypeSymbol[] ParamTypes,
    bool IsAfter);

internal sealed record MapperClass(
    string Namespace,
    System.Collections.Generic.IReadOnlyList<MappingDecl> Mappings,
    bool CaseInsensitive = false,
    bool StrictSource = false,
    System.Collections.Generic.IReadOnlyList<HookMethod>? Hooks = null,
    string? Culture = null,
    System.Collections.Generic.IReadOnlyList<PolymorphicDecl>? PolymorphicDecls = null,
    Microsoft.CodeAnalysis.INamedTypeSymbol? TypeSymbol = null,
    bool SkipCollectionOverloads = false);

/// <summary>
/// One <c>[Map&lt;,&gt;]</c> or <c>[TryMap&lt;,&gt;]</c> mapping, with its source and destination types
/// as the symbols the attribute names.
/// </summary>
/// <remarks>
/// The model lives only inside the host's transform, which does all of the semantic work and
/// hands the pipeline a value-only <see cref="MapperOutput"/>. Holding the symbols is what lets
/// the emitters work on any type the attribute can name: a display string such as
/// <c>global::App.Dto.A</c> or <c>global::App.G&lt;int&gt;</c> is not a metadata name, so it cannot be
/// resolved again. The display strings are derived from the symbols and used only to write code
/// and to compare mappings.
/// </remarks>
internal sealed record MappingDecl(
    Microsoft.CodeAnalysis.INamedTypeSymbol SourceTypeSymbol,
    Microsoft.CodeAnalysis.INamedTypeSymbol DestinationTypeSymbol,
    MappingKind Kind,
    Microsoft.CodeAnalysis.Location Location,
    Microsoft.CodeAnalysis.IMethodSymbol? UserPartialMethod = null,
    bool FromReverse = false,
    Microsoft.CodeAnalysis.IMethodSymbol? UpdateInPlacePartial = null,
    bool Projection = false,
    bool CycleSafe = false,
    bool DeepClone = false)
{
    public string SourceTypeFqn { get; } =
        SourceTypeSymbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);

    public string DestinationTypeFqn { get; } =
        DestinationTypeSymbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
}

/// <summary>
/// One <c>[PolymorphicMap&lt;,&gt;]</c> or <c>[PolymorphicTryMap&lt;,&gt;]</c> dispatcher, with its base
/// types as symbols for the same reason as <see cref="MappingDecl"/>.
/// </summary>
internal sealed record PolymorphicDecl(
    Microsoft.CodeAnalysis.INamedTypeSymbol BaseTypeSymbol,
    Microsoft.CodeAnalysis.INamedTypeSymbol BaseDestinationTypeSymbol,
    MappingKind Kind,
    Microsoft.CodeAnalysis.Location Location)
{
    public string BaseTypeFqn { get; } =
        BaseTypeSymbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);

    public string BaseDestinationTypeFqn { get; } =
        BaseDestinationTypeSymbol.ToDisplayString(Microsoft.CodeAnalysis.SymbolDisplayFormat.FullyQualifiedFormat);
}
