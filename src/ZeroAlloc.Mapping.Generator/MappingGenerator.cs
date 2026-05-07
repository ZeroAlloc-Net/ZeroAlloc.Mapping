using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class MappingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, (spc, comp) =>
        {
            // ZAMP006 — non-static-partial class hosts get reported during discovery.
            ReportNonStaticPartialHosts(spc, comp);

            var classes = MapperDiscovery.Discover(comp).ToList();
            if (classes.Count == 0) return;

            foreach (var c in classes)
            {
                ReportPerClassDiagnostics(spc, c, comp);
                var src = MapEmitter.Emit(c, comp);
                spc.AddSource($"{c.ClassName}.g.cs", src);
            }
        });
    }

    private static void ReportNonStaticPartialHosts(SourceProductionContext spc, Compilation comp)
    {
        var mapAttr = comp.GetTypeByMetadataName(MapperDiscovery.MapAttributeFqn);
        var tryMapAttr = comp.GetTypeByMetadataName(MapperDiscovery.TryMapAttributeFqn);
        if (mapAttr is null && tryMapAttr is null) return;

        foreach (var type in EnumerateTypes(comp.GlobalNamespace))
        {
            var hasAny = type.GetAttributes().Any(a =>
            {
                var orig = a.AttributeClass?.OriginalDefinition;
                return (mapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, mapAttr))
                    || (tryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, tryMapAttr));
            });
            if (!hasAny) continue;

            var isStaticPartial = type.IsStatic && type.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax c &&
                c.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));

            if (!isStaticPartial)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ZAMP006_NotStaticPartialClass,
                    type.Locations.FirstOrDefault() ?? Location.None,
                    type.ToDisplayString()));
            }
        }
    }

    private static void ReportPerClassDiagnostics(SourceProductionContext spc, MapperClass cls, Compilation comp)
    {
        foreach (var decl in cls.Mappings)
        {
            var src = comp.GetTypeByMetadataName(StripGlobal(decl.SourceTypeFqn));
            var dst = comp.GetTypeByMetadataName(StripGlobal(decl.DestinationTypeFqn));
            if (src is null || dst is null) continue;

            var match = PropertyMatcher.Match(src, dst, decl.UserPartialMethod, cls.CaseInsensitive);
            if (match is null) continue;

            // ZAMP011 — under [CaseInsensitiveMapping], source has two properties whose names
            // collide case-insensitively and the destination has a constructor param matching them.
            if (cls.CaseInsensitive)
            {
                var grouped = src.GetMembers().OfType<IPropertySymbol>()
                    .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                    .GroupBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1);
                foreach (var group in grouped)
                {
                    if (match.Constructor.Parameters.Any(p => string.Equals(p.Name, group.Key, System.StringComparison.OrdinalIgnoreCase)))
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ZAMP011_CaseInsensitiveAmbiguous,
                            decl.Location,
                            group.First().Name));
                    }
                }
            }

            // ZAMP001 — unmatched required destination params (those without [MapValue] or source).
            foreach (var unmatched in match.UnmatchedTargetParams)
            {
                if (decl.Kind == MappingKind.Map)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP001_DestinationHasNoSource,
                        decl.Location,
                        unmatched, dst.ToDisplayString()));
                }
            }

            // ZAMP002 — no conversion path on a matched pair.
            foreach (var m in match.Mappings)
            {
                var conv = ConversionResolver.Resolve(m.SourceType, m.TargetType, comp);
                if (conv.Kind == ConversionKind.None &&
                    NestedMappingResolver.FindNestedMapper(cls, m.SourceType, m.TargetType) is null &&
                    NestedMappingResolver.AsCollection(m.SourceType) is null)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP002_NoConversionPath,
                        decl.Location,
                        m.TargetParamName,
                        m.SourceType.ToDisplayString(),
                        m.TargetType.ToDisplayString()));
                }
            }

            // ZAMP007 — under [Map], nullable source vs non-nullable dest.
            if (decl.Kind == MappingKind.Map)
            {
                foreach (var m in match.Mappings)
                {
                    if (m.SourceType.NullableAnnotation == NullableAnnotation.Annotated &&
                        m.TargetType.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                        m.TargetType.IsReferenceType)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ZAMP007_NullableMismatch,
                            decl.Location,
                            m.TargetParamName));
                    }
                }
            }

            // ZAMP004 — [Map] chains to nested mapper that is [TryMap]-only.
            if (decl.Kind == MappingKind.Map)
            {
                foreach (var m in match.Mappings)
                {
                    var nested = NestedMappingResolver.FindNestedMapper(cls, m.SourceType, m.TargetType);
                    if (nested is { Kind: MappingKind.TryMap })
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ZAMP004_MapChainsTryMap,
                            decl.Location,
                            m.SourceType.ToDisplayString(),
                            m.TargetType.ToDisplayString()));
                    }
                }
            }

            // ZAMP005 — [MapProperty] references missing property name.
            if (decl.UserPartialMethod is not null)
            {
                var sourceProps = new System.Collections.Generic.HashSet<string>(
                    src.GetMembers().OfType<IPropertySymbol>()
                        .Where(p => p.DeclaredAccessibility == Accessibility.Public)
                        .Select(p => p.Name),
                    System.StringComparer.Ordinal);
                var ctorParams = new System.Collections.Generic.HashSet<string>(
                    match.Constructor.Parameters.Select(p => p.Name),
                    System.StringComparer.Ordinal);

                foreach (var attr in decl.UserPartialMethod.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "MapPropertyAttribute" && attr.ConstructorArguments.Length == 2)
                    {
                        var srcName = attr.ConstructorArguments[0].Value as string;
                        var dstName = attr.ConstructorArguments[1].Value as string;
                        if (srcName is not null && !sourceProps.Contains(srcName))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                decl.Location, srcName, src.ToDisplayString()));
                        }
                        if (dstName is not null && !ctorParams.Contains(dstName))
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                decl.Location, dstName, dst.ToDisplayString()));
                        }
                    }
                }
            }

            // ZAMP003 — ambiguous source after [MapProperty] (rename collides with by-name match).
            if (decl.UserPartialMethod is not null)
            {
                var renames = decl.UserPartialMethod.GetAttributes()
                    .Where(a => a.AttributeClass?.Name == "MapPropertyAttribute" && a.ConstructorArguments.Length == 2)
                    .Select(a => (Source: a.ConstructorArguments[0].Value as string, Target: a.ConstructorArguments[1].Value as string))
                    .Where(p => p.Source is not null && p.Target is not null)
                    .ToList();
                var ctorParamNames = new System.Collections.Generic.HashSet<string>(
                    match.Constructor.Parameters.Select(p => p.Name), System.StringComparer.Ordinal);
                var sourcePropSet = new System.Collections.Generic.HashSet<string>(
                    src.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name), System.StringComparer.Ordinal);

                foreach (var rename in renames)
                {
                    if (rename.Target is { } target && ctorParamNames.Contains(target) && sourcePropSet.Contains(target) && rename.Source != target)
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ZAMP003_AmbiguousSource,
                            decl.Location, target));
                    }
                }
            }

            // ZAMP008 — multiple non-copy public ctors with equal arity.
            if (dst is INamedTypeSymbol nt)
            {
                var nonCopy = nt.InstanceConstructors
                    .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                    .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, nt)))
                    .ToList();
                var maxArity = nonCopy.Select(c => c.Parameters.Length).DefaultIfEmpty(0).Max();
                if (nonCopy.Count(c => c.Parameters.Length == maxArity) > 1)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP008_AmbiguousConstructor,
                        decl.Location, dst.ToDisplayString()));
                }
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var member in ns.GetMembers())
        {
            if (member is INamedTypeSymbol t)
            {
                yield return t;
                foreach (var nested in EnumerateNested(t)) yield return nested;
            }
            else if (member is INamespaceSymbol child)
            {
                foreach (var nested in EnumerateTypes(child)) yield return nested;
            }
        }
    }

    private static System.Collections.Generic.IEnumerable<INamedTypeSymbol> EnumerateNested(INamedTypeSymbol t)
    {
        foreach (var nested in t.GetTypeMembers())
        {
            yield return nested;
            foreach (var deeper in EnumerateNested(nested)) yield return deeper;
        }
    }

    private static string StripGlobal(string fqn) =>
        fqn.StartsWith("global::", System.StringComparison.Ordinal) ? fqn.Substring(8) : fqn;
}
