using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// The partial declarations a host's generated members are emitted into: those of its
/// containing types, outermost first, then the host's own.
/// </summary>
internal static class HostDeclarations
{
    /// <summary>
    /// The outermost containing type of <paramref name="host"/> that is not declared
    /// <c>partial</c>, or null when all of them are. The generated code has to reopen every
    /// containing type, which only a partial type allows.
    /// </summary>
    public static INamedTypeSymbol? FirstNonPartialContainingType(INamedTypeSymbol host)
    {
        INamedTypeSymbol? outermost = null;
        for (var t = host.ContainingType; t is not null; t = t.ContainingType)
        {
            if (!IsPartial(t)) outermost = t;
        }
        return outermost;
    }

    /// <summary>
    /// Opens the partial declarations of the containing types and of the host, outermost
    /// first, and returns how many it opened, so the caller closes as many.
    /// </summary>
    public static int Open(StringBuilder sb, INamedTypeSymbol host)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = host; t is not null; t = t.ContainingType) chain.Add(t);
        chain.Reverse();

        foreach (var type in chain)
        {
            if (type.IsRefLikeType) sb.Append("ref ");
            sb.Append("partial ").Append(Keyword(type)).Append(' ').Append(Identifier(type.Name));
            AppendTypeParameters(sb, type);
            sb.Append("\n{\n");
        }
        return chain.Count;
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword)));

    private static string Keyword(INamedTypeSymbol type) => type switch
    {
        { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        { IsRecord: true } => "record",
        { TypeKind: TypeKind.Struct } => "struct",
        { TypeKind: TypeKind.Interface } => "interface",
        _ => "class",
    };

    /// <summary>
    /// Type parameter names only. A partial part may leave out constraints, and variance
    /// never appears here: an interface with a variant type parameter cannot contain types.
    /// </summary>
    private static void AppendTypeParameters(StringBuilder sb, INamedTypeSymbol type)
    {
        if (type.TypeParameters.Length == 0) return;
        sb.Append('<');
        for (var i = 0; i < type.TypeParameters.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(Identifier(type.TypeParameters[i].Name));
        }
        sb.Append('>');
    }

    private static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}
