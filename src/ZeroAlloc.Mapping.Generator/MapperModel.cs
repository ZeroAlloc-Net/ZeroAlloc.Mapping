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
    string ClassName,
    System.Collections.Generic.IReadOnlyList<MappingDecl> Mappings,
    bool CaseInsensitive = false,
    bool StrictSource = false,
    System.Collections.Generic.IReadOnlyList<HookMethod>? Hooks = null,
    string? Culture = null,
    System.Collections.Generic.IReadOnlyList<PolymorphicDecl>? PolymorphicDecls = null,
    Microsoft.CodeAnalysis.INamedTypeSymbol? TypeSymbol = null,
    bool SkipCollectionOverloads = false);

internal sealed record MappingDecl(
    string SourceTypeFqn,
    string DestinationTypeFqn,
    MappingKind Kind,
    Microsoft.CodeAnalysis.Location Location,
    Microsoft.CodeAnalysis.IMethodSymbol? UserPartialMethod = null,
    bool FromReverse = false,
    Microsoft.CodeAnalysis.IMethodSymbol? UpdateInPlacePartial = null,
    Microsoft.CodeAnalysis.INamedTypeSymbol? SourceTypeSymbol = null,
    Microsoft.CodeAnalysis.INamedTypeSymbol? DestinationTypeSymbol = null);

internal sealed record PolymorphicDecl(
    string BaseTypeFqn,
    string BaseDestinationTypeFqn,
    MappingKind Kind,
    Microsoft.CodeAnalysis.Location Location,
    Microsoft.CodeAnalysis.INamedTypeSymbol BaseTypeSymbol,
    Microsoft.CodeAnalysis.INamedTypeSymbol BaseDestinationTypeSymbol);
