using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal static class NestedMappingResolver
{
    public static MappingDecl? FindNestedMapper(
        MapperClass owningClass,
        ITypeSymbol source,
        ITypeSymbol destination)
    {
        foreach (var decl in owningClass.Mappings)
        {
            var srcFqn = source.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var dstFqn = destination.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (decl.SourceTypeFqn == srcFqn && decl.DestinationTypeFqn == dstFqn)
                return decl;
        }
        return null;
    }

    public static (ITypeSymbol Element, string CollectionKind)? AsCollection(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol arr)
            return (arr.ElementType, "array");

        if (type is INamedTypeSymbol nt && nt.IsGenericType && nt.TypeArguments.Length == 1)
        {
            var fqn = nt.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            switch (fqn)
            {
                case "global::System.Collections.Generic.List<T>":
                case "global::System.Collections.Generic.IList<T>":
                case "global::System.Collections.Generic.IReadOnlyList<T>":
                case "global::System.Collections.Generic.ICollection<T>":
                case "global::System.Collections.Generic.IReadOnlyCollection<T>":
                case "global::System.Collections.Generic.IEnumerable<T>":
                    return (nt.TypeArguments[0], "list");
            }
        }

        return null;
    }
}
