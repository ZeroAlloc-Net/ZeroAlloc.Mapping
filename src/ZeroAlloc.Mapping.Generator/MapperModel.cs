namespace ZeroAlloc.Mapping.Generator;

internal enum MappingKind
{
    Map,
    TryMap
}

internal sealed record MapperClass(
    string Namespace,
    string ClassName,
    System.Collections.Generic.IReadOnlyList<MappingDecl> Mappings,
    bool CaseInsensitive = false,
    bool StrictSource = false);

internal sealed record MappingDecl(
    string SourceTypeFqn,
    string DestinationTypeFqn,
    MappingKind Kind,
    Microsoft.CodeAnalysis.Location Location,
    Microsoft.CodeAnalysis.IMethodSymbol? UserPartialMethod = null);
