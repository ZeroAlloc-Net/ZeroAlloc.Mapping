using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal static class MapperDiscovery
{
    public const string MapAttributeFqn = "ZeroAlloc.Mapping.MapAttribute`2";
    public const string TryMapAttributeFqn = "ZeroAlloc.Mapping.TryMapAttribute`2";
    public const string ReverseMapAttributeFqn = "ZeroAlloc.Mapping.ReverseMapAttribute`2";
    public const string ReverseTryMapAttributeFqn = "ZeroAlloc.Mapping.ReverseTryMapAttribute`2";
    public const string PolymorphicMapAttributeFqn = "ZeroAlloc.Mapping.PolymorphicMapAttribute`2";
    public const string PolymorphicTryMapAttributeFqn = "ZeroAlloc.Mapping.PolymorphicTryMapAttribute`2";

    /// <summary>
    /// The mapping attribute types this compilation can see, in the order discovery checks them.
    /// </summary>
    internal sealed class AttributeSymbols
    {
        public AttributeSymbols(Compilation comp)
        {
            Map = comp.GetTypeByMetadataName(MapAttributeFqn);
            TryMap = comp.GetTypeByMetadataName(TryMapAttributeFqn);
            ReverseMap = comp.GetTypeByMetadataName(ReverseMapAttributeFqn);
            ReverseTryMap = comp.GetTypeByMetadataName(ReverseTryMapAttributeFqn);
            PolymorphicMap = comp.GetTypeByMetadataName(PolymorphicMapAttributeFqn);
            PolymorphicTryMap = comp.GetTypeByMetadataName(PolymorphicTryMapAttributeFqn);
        }

        public INamedTypeSymbol? Map { get; }
        public INamedTypeSymbol? TryMap { get; }
        public INamedTypeSymbol? ReverseMap { get; }
        public INamedTypeSymbol? ReverseTryMap { get; }
        public INamedTypeSymbol? PolymorphicMap { get; }
        public INamedTypeSymbol? PolymorphicTryMap { get; }

        public bool IsMappingAttribute(AttributeData attribute)
        {
            var orig = attribute.AttributeClass?.OriginalDefinition;
            if (orig is null) return false;
            return Is(orig, Map) || Is(orig, TryMap) || Is(orig, ReverseMap) || Is(orig, ReverseTryMap)
                || Is(orig, PolymorphicMap) || Is(orig, PolymorphicTryMap);
        }

        private static bool Is(INamedTypeSymbol orig, INamedTypeSymbol? attribute) =>
            attribute is not null && SymbolEqualityComparer.Default.Equals(orig, attribute);
    }

    /// <summary>
    /// Reads the mappings a static partial class declares. Returns null when it declares none.
    /// </summary>
    public static MapperClass? DiscoverHost(INamedTypeSymbol type, AttributeSymbols attributes)
    {
        var mapAttr = attributes.Map;
        var tryMapAttr = attributes.TryMap;
        var reverseMapAttr = attributes.ReverseMap;
        var reverseTryMapAttr = attributes.ReverseTryMap;
        var polymorphicMapAttr = attributes.PolymorphicMap;
        var polymorphicTryMapAttr = attributes.PolymorphicTryMap;

        var decls = new System.Collections.Generic.List<MappingDecl>();
        var polymorphics = new System.Collections.Generic.List<PolymorphicDecl>();
        foreach (var attr in type.GetAttributes())
        {
            var orig = attr.AttributeClass?.OriginalDefinition;
            if (orig is null) continue;

            if (polymorphicMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, polymorphicMapAttr))
            {
                var polyArgs = attr.AttributeClass!.TypeArguments;
                if (polyArgs.Length != 2) continue;
                if (polyArgs[0] is INamedTypeSymbol pBase && polyArgs[1] is INamedTypeSymbol pBaseDst)
                {
                    polymorphics.Add(new PolymorphicDecl(
                        BaseTypeFqn: pBase.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BaseDestinationTypeFqn: pBaseDst.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.Map,
                        Location: AttributeLocation(attr, type.Locations[0]),
                        BaseTypeSymbol: pBase,
                        BaseDestinationTypeSymbol: pBaseDst));
                }
                continue;
            }
            if (polymorphicTryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, polymorphicTryMapAttr))
            {
                var polyArgs = attr.AttributeClass!.TypeArguments;
                if (polyArgs.Length != 2) continue;
                if (polyArgs[0] is INamedTypeSymbol pBase && polyArgs[1] is INamedTypeSymbol pBaseDst)
                {
                    polymorphics.Add(new PolymorphicDecl(
                        BaseTypeFqn: pBase.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        BaseDestinationTypeFqn: pBaseDst.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        Kind: MappingKind.TryMap,
                        Location: AttributeLocation(attr, type.Locations[0]),
                        BaseTypeSymbol: pBase,
                        BaseDestinationTypeSymbol: pBaseDst));
                }
                continue;
            }

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
                    Location: AttributeLocation(attr, type.Locations[0]),
                    UserPartialMethod: fwdPartial,
                    FromReverse: true,
                    SourceTypeSymbol: reverseTypeArgs[0] as INamedTypeSymbol,
                    DestinationTypeSymbol: reverseTypeArgs[1] as INamedTypeSymbol));
                decls.Add(new MappingDecl(
                    SourceTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    DestinationTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Kind: MappingKind.Map,
                    Location: AttributeLocation(attr, type.Locations[0]),
                    UserPartialMethod: revPartial,
                    FromReverse: true,
                    SourceTypeSymbol: reverseTypeArgs[1] as INamedTypeSymbol,
                    DestinationTypeSymbol: reverseTypeArgs[0] as INamedTypeSymbol));
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
                    Location: AttributeLocation(attr, type.Locations[0]),
                    UserPartialMethod: fwdPartial,
                    FromReverse: true,
                    SourceTypeSymbol: reverseTypeArgs[0] as INamedTypeSymbol,
                    DestinationTypeSymbol: reverseTypeArgs[1] as INamedTypeSymbol));
                decls.Add(new MappingDecl(
                    SourceTypeFqn: reverseTypeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    DestinationTypeFqn: reverseTypeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    Kind: MappingKind.TryMap,
                    Location: AttributeLocation(attr, type.Locations[0]),
                    UserPartialMethod: revPartial,
                    FromReverse: true,
                    SourceTypeSymbol: reverseTypeArgs[1] as INamedTypeSymbol,
                    DestinationTypeSymbol: reverseTypeArgs[0] as INamedTypeSymbol));
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

            var projection = kind == MappingKind.Map && attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "Projection", System.StringComparison.Ordinal))
                .Value.Value is true;
            var cycleSafe = kind == MappingKind.Map && attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "CycleSafe", System.StringComparison.Ordinal))
                .Value.Value is true;
            var deepClone = kind == MappingKind.Map && attr.NamedArguments
                .FirstOrDefault(kv => string.Equals(kv.Key, "DeepClone", System.StringComparison.Ordinal))
                .Value.Value is true;

            decls.Add(new MappingDecl(
                SourceTypeFqn: typeArgs[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                DestinationTypeFqn: typeArgs[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Kind: kind,
                Location: AttributeLocation(attr, type.Locations[0]),
                UserPartialMethod: userPartial,
                UpdateInPlacePartial: updateInPlace,
                Projection: projection,
                CycleSafe: cycleSafe,
                DeepClone: deepClone,
                SourceTypeSymbol: typeArgs[0] as INamedTypeSymbol,
                DestinationTypeSymbol: typeArgs[1] as INamedTypeSymbol));
        }

        if (decls.Count == 0 && polymorphics.Count == 0) return null;

        var caseInsensitive = type.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "CaseInsensitiveMappingAttribute" } ac &&
            ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" });

        var strictSource = type.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "StrictSourceMappingAttribute" } ac &&
            ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" });

        var skipCollectionOverloads = type.GetAttributes().Any(a =>
            a.AttributeClass is { Name: "SkipCollectionOverloadsAttribute" } ac &&
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

        return new MapperClass(
            Namespace: type.ContainingNamespace.IsGlobalNamespace
                ? "" : type.ContainingNamespace.ToDisplayString(),
            ClassName: type.Name,
            Mappings: decls,
            CaseInsensitive: caseInsensitive,
            StrictSource: strictSource,
            Hooks: hooks,
            Culture: culture,
            PolymorphicDecls: polymorphics.Count > 0 ? polymorphics : null,
            TypeSymbol: type,
            SkipCollectionOverloads: skipCollectionOverloads);
    }

    /// <summary>
    /// The location of an attribute as it is written, <c>Map&lt;Src, Dst&gt;(...)</c> without the
    /// brackets, so a diagnostic about one mapping is not shared with the other mappings of its
    /// class and a <c>#pragma warning disable</c> can silence it alone. An attribute declared in
    /// source always has syntax; the fallback is for one that is not.
    /// </summary>
    internal static Location AttributeLocation(AttributeData attribute, Location fallback) =>
        attribute.ApplicationSyntaxReference is { } syntax
            ? Location.Create(syntax.SyntaxTree, syntax.Span)
            : fallback;

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

    internal static bool IsStaticPartialClass(INamedTypeSymbol t) =>
        t.IsStatic && t.DeclaringSyntaxReferences.Any(r =>
            r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c &&
            c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));
}
