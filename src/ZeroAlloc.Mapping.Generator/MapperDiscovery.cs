using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal static class MapperDiscovery
{
    public const string MapAttributeFqn = "ZeroAlloc.Mapping.MapAttribute`2";
    public const string TryMapAttributeFqn = "ZeroAlloc.Mapping.TryMapAttribute`2";
    public const string ReverseMapAttributeFqn = "ZeroAlloc.Mapping.ReverseMapAttribute`2";
    public const string ReverseTryMapAttributeFqn = "ZeroAlloc.Mapping.ReverseTryMapAttribute`2";

    public static System.Collections.Generic.IEnumerable<MapperClass> Discover(Compilation comp)
    {
        var mapAttr = comp.GetTypeByMetadataName(MapAttributeFqn);
        var tryMapAttr = comp.GetTypeByMetadataName(TryMapAttributeFqn);
        var reverseMapAttr = comp.GetTypeByMetadataName(ReverseMapAttributeFqn);
        var reverseTryMapAttr = comp.GetTypeByMetadataName(ReverseTryMapAttributeFqn);
        if (mapAttr is null && tryMapAttr is null && reverseMapAttr is null && reverseTryMapAttr is null) yield break;

        foreach (var type in EnumerateTypes(comp.GlobalNamespace))
        {
            if (!IsStaticPartialClass(type)) continue;

            var decls = new System.Collections.Generic.List<MappingDecl>();
            foreach (var attr in type.GetAttributes())
            {
                var orig = attr.AttributeClass?.OriginalDefinition;
                if (orig is null) continue;

                if (reverseMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, reverseMapAttr))
                {
                    var reverseTypeArgs = attr.AttributeClass!.TypeArguments;
                    if (reverseTypeArgs.Length != 2) continue;
                    var fwdPartial = FindUserPartialMethod(type, MappingKind.Map, reverseTypeArgs[0], reverseTypeArgs[1]);
                    var revPartial = FindUserPartialMethod(type, MappingKind.Map, reverseTypeArgs[1], reverseTypeArgs[0]);
                    decls.Add(new MappingDecl(
                        SourceTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        DestinationTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.Map,
                        Location: type.Locations.FirstOrDefault() ?? Location.None,
                        UserPartialMethod: fwdPartial,
                        FromReverse: true));
                    decls.Add(new MappingDecl(
                        SourceTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        DestinationTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.Map,
                        Location: type.Locations.FirstOrDefault() ?? Location.None,
                        UserPartialMethod: revPartial,
                        FromReverse: true));
                    continue;
                }
                if (reverseTryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, reverseTryMapAttr))
                {
                    var reverseTypeArgs = attr.AttributeClass!.TypeArguments;
                    if (reverseTypeArgs.Length != 2) continue;
                    var fwdPartial = FindUserPartialMethod(type, MappingKind.TryMap, reverseTypeArgs[0], reverseTypeArgs[1]);
                    var revPartial = FindUserPartialMethod(type, MappingKind.TryMap, reverseTypeArgs[1], reverseTypeArgs[0]);
                    decls.Add(new MappingDecl(
                        SourceTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        DestinationTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.TryMap,
                        Location: type.Locations.FirstOrDefault() ?? Location.None,
                        UserPartialMethod: fwdPartial,
                        FromReverse: true));
                    decls.Add(new MappingDecl(
                        SourceTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        DestinationTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.TryMap,
                        Location: type.Locations.FirstOrDefault() ?? Location.None,
                        UserPartialMethod: revPartial,
                        FromReverse: true));
                    continue;
                }

                MappingKind kind;
                if (mapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, mapAttr))
                    kind = MappingKind.Map;
                else if (tryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, tryMapAttr))
                    kind = MappingKind.TryMap;
                else
                    continue;

                var typeArgs = attr.AttributeClass!.TypeArguments;
                if (typeArgs.Length != 2) continue;

                var userPartial = FindUserPartialMethod(type, kind, typeArgs[0], typeArgs[1]);

                var updateInPlace = kind == MappingKind.Map
                    ? FindUpdateInPlacePartial(type, typeArgs[0], typeArgs[1])
                    : null;

                decls.Add(new MappingDecl(
                    SourceTypeFqn: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    DestinationTypeFqn: typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Kind: kind,
                    Location: type.Locations.FirstOrDefault() ?? Location.None,
                    UserPartialMethod: userPartial,
                    UpdateInPlacePartial: updateInPlace));
            }

            if (decls.Count == 0) continue;

            var caseInsensitive = type.GetAttributes().Any(a =>
                a.AttributeClass is { Name: "CaseInsensitiveMappingAttribute" } ac &&
                ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" });

            var strictSource = type.GetAttributes().Any(a =>
                a.AttributeClass is { Name: "StrictSourceMappingAttribute" } ac &&
                ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" });

            var hooks = new System.Collections.Generic.List<HookMethod>();
            foreach (var m in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsStatic))
            {
                foreach (var attr in m.GetAttributes())
                {
                    var ac = attr.AttributeClass;
                    var isBefore = ac is { Name: "BeforeMapAttribute" } &&
                                   ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" };
                    var isAfter = ac is { Name: "AfterMapAttribute" } &&
                                  ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" };
                    if (!isBefore && !isAfter) continue;
                    hooks.Add(new HookMethod(
                        MethodName: m.Name,
                        ParamTypes: m.Parameters.Select(p => p.Type).ToArray(),
                        IsAfter: isAfter));
                }
            }

            string? culture = null;
            foreach (var a in type.GetAttributes())
            {
                if (a.AttributeClass is { Name: "MappingCultureAttribute" } ac &&
                    ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" } &&
                    a.ConstructorArguments.Length == 1 &&
                    a.ConstructorArguments[0].Value is string s)
                {
                    culture = s;
                    break;
                }
            }

            yield return new MapperClass(
                Namespace: type.ContainingNamespace.IsGlobalNamespace
                    ? "" : type.ContainingNamespace.ToDisplayString(),
                ClassName: type.Name,
                Mappings: decls,
                CaseInsensitive: caseInsensitive,
                StrictSource: strictSource,
                Hooks: hooks,
                Culture: culture);
        }
    }

    private static IMethodSymbol? FindUserPartialMethod(INamedTypeSymbol owner, MappingKind kind, ITypeSymbol src, ITypeSymbol dst)
    {
        var name = kind == MappingKind.Map ? "Map" : "TryMap";
        foreach (var m in owner.GetMembers(name).OfType<IMethodSymbol>())
        {
            if (!m.IsStatic) continue;
            if (m.Parameters.Length != 1) continue;
            if (!SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, src)) continue;
            if (kind == MappingKind.Map && !SymbolEqualityComparer.Default.Equals(m.ReturnType, dst)) continue;
            return m;
        }
        return null;
    }

    private static IMethodSymbol? FindUpdateInPlacePartial(INamedTypeSymbol owner, ITypeSymbol src, ITypeSymbol dst)
    {
        foreach (var m in owner.GetMembers("Map").OfType<IMethodSymbol>())
        {
            if (!m.IsStatic) continue;
            if (!m.ReturnsVoid) continue;
            if (m.Parameters.Length != 2) continue;
            if (!SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, src)) continue;
            if (!SymbolEqualityComparer.Default.Equals(m.Parameters[1].Type, dst)) continue;
            var isPartial = m.IsPartialDefinition || m.PartialDefinitionPart is not null
                || m.DeclaringSyntaxReferences.Any(r =>
                    r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax mds &&
                    mds.Modifiers.Any(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));
            if (!isPartial) continue;
            return m;
        }
        return null;
    }

    private static bool IsStaticPartialClass(INamedTypeSymbol t) =>
        t.IsStatic && t.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c &&
            c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamedTypeSymbol t)
            {
                yield return t;
                foreach (var nested in EnumerateNested(t))
                    yield return nested;
            }
            else if (member is INamespaceSymbol child)
            {
                foreach (var nested in EnumerateTypes(child))
                    yield return nested;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol t)
    {
        foreach (var nested in t.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNested(nested))
                yield return deeper;
        }
    }
}
