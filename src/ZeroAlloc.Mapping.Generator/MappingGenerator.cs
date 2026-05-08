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
        var reverseMapAttr = comp.GetTypeByMetadataName(MapperDiscovery.ReverseMapAttributeFqn);
        var reverseTryMapAttr = comp.GetTypeByMetadataName(MapperDiscovery.ReverseTryMapAttributeFqn);
        var polymorphicMapAttr = comp.GetTypeByMetadataName(MapperDiscovery.PolymorphicMapAttributeFqn);
        var polymorphicTryMapAttr = comp.GetTypeByMetadataName(MapperDiscovery.PolymorphicTryMapAttributeFqn);
        if (mapAttr is null && tryMapAttr is null && reverseMapAttr is null && reverseTryMapAttr is null
            && polymorphicMapAttr is null && polymorphicTryMapAttr is null) return;

        foreach (var type in EnumerateTypes(comp.GlobalNamespace))
        {
            var hasAny = type.GetAttributes().Any(a =>
            {
                var orig = a.AttributeClass?.OriginalDefinition;
                return (mapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, mapAttr))
                    || (tryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, tryMapAttr))
                    || (reverseMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, reverseMapAttr))
                    || (reverseTryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, reverseTryMapAttr))
                    || (polymorphicMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, polymorphicMapAttr))
                    || (polymorphicTryMapAttr is not null && SymbolEqualityComparer.Default.Equals(orig, polymorphicTryMapAttr));
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
        // ZAMP016 — duplicate [MappingCulture] across partial parts.
        if (cls.Culture is not null && cls.TypeSymbol is not null)
        {
            var cultureCount = 0;
            foreach (var a in cls.TypeSymbol.GetAttributes())
            {
                if (a.AttributeClass is { Name: "MappingCultureAttribute" } ac &&
                    ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" })
                {
                    cultureCount++;
                }
            }
            if (cultureCount > 1)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.ZAMP016_DuplicateMappingCulture,
                    cls.TypeSymbol.Locations.FirstOrDefault() ?? Location.None,
                    cls.TypeSymbol.ToDisplayString(),
                    cls.Culture));
            }
        }

        foreach (var decl in cls.Mappings)
        {
            var src = comp.GetTypeByMetadataName(StripGlobal(decl.SourceTypeFqn));
            var dst = comp.GetTypeByMetadataName(StripGlobal(decl.DestinationTypeFqn));
            if (src is null || dst is null) continue;

            // ZAMP009 — [ReverseMap]/[ReverseTryMap] desugared decls cannot be auto-reversed
            // when the user-declared partial carries information-asymmetric customisations.
            if (decl.FromReverse && decl.UserPartialMethod is not null)
            {
                foreach (var attr in decl.UserPartialMethod.GetAttributes())
                {
                    var name = attr.AttributeClass?.Name;
                    if (name == "MapPropertyAttribute" || name == "MapValueAttribute" || name == "MapperIgnoreTargetAttribute")
                    {
                        spc.ReportDiagnostic(Diagnostic.Create(
                            Diagnostics.ZAMP009_ReverseMapNotSymmetric,
                            decl.Location,
                            src.ToDisplayString(),
                            dst.ToDisplayString(),
                            "[" + name!.Replace("Attribute", "") + "]"));
                        break;
                    }
                }
            }

            var match = PropertyMatcher.Match(src, dst, decl.UserPartialMethod, cls.CaseInsensitive);
            if (match is null) continue;

            // ZAMP011 — under [CaseInsensitiveMapping], source has two properties whose names
            // collide case-insensitively and the destination has a constructor param matching them.
            if (cls.CaseInsensitive)
            {
                var grouped = PropertyMatcher.GetAllPublicProperties(src)
                    .Where(p => !PropertyMatcher.IsObsolete(p))
                    .GroupBy(p => p.Name, System.StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1);
                foreach (var group in grouped)
                {
                    var matchingParam = match.Constructor.Parameters
                        .FirstOrDefault(p => string.Equals(p.Name, group.Key, System.StringComparison.OrdinalIgnoreCase));
                    if (matchingParam is null) continue;
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP011_CaseInsensitiveAmbiguous,
                        decl.Location,
                        matchingParam.Name));
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
                    PropertyMatcher.GetAllPublicProperties(src).Select(p => p.Name),
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
                        if (srcName is not null)
                        {
                            if (srcName.Contains('.'))
                            {
                                INamedTypeSymbol? cursor = src;
                                foreach (var segment in srcName.Split('.'))
                                {
                                    if (cursor is null) break;
                                    var found = PropertyMatcher.GetAllPublicProperties(cursor)
                                        .FirstOrDefault(p => p.Name == segment);
                                    if (found is null)
                                    {
                                        spc.ReportDiagnostic(Diagnostic.Create(
                                            Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                            decl.Location, segment, cursor.ToDisplayString()));
                                        break;
                                    }
                                    cursor = found.Type as INamedTypeSymbol;
                                }
                            }
                            else if (!sourceProps.Contains(srcName))
                            {
                                spc.ReportDiagnostic(Diagnostic.Create(
                                    Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                    decl.Location, srcName, src.ToDisplayString()));
                            }
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
                    PropertyMatcher.GetAllPublicProperties(src).Select(p => p.Name), System.StringComparer.Ordinal);

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

            // ZAMP010 — under [StrictSourceMapping], every source prop must be consumed
            // by a destination param or marked [MapperIgnoreSource].
            if (cls.StrictSource)
            {
                var consumed = new System.Collections.Generic.HashSet<string>(
                    match.Mappings.Select(m => m.SourcePropertyName), System.StringComparer.Ordinal);
                foreach (var p in PropertyMatcher.GetAllPublicProperties(src))
                {
                    if (consumed.Contains(p.Name)) continue;
                    if (PropertyMatcher.IsObsolete(p)) continue;
                    var ignore = p.GetAttributes().Any(a =>
                        a.AttributeClass is { Name: "MapperIgnoreSourceAttribute" } ac &&
                        ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" });
                    if (ignore) continue;
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP010_UnconsumedSource,
                        decl.Location, p.Name, src.ToDisplayString()));
                }
            }

            // ZAMP012 — update-in-place void overload requested but a matched destination
            // property has no public setter (init-only or read-only). Walk the in-place
            // matcher's results so this also fires for parameterless-ctor POCOs whose
            // init-only properties don't appear in match.Mappings (constructor-form).
            if (decl.UpdateInPlacePartial is not null && decl.Kind == MappingKind.Map)
            {
                var inPlace = MapEmitter.MatchUpdateInPlace(src, dst, decl.UpdateInPlacePartial, cls.CaseInsensitive);
                if (inPlace is not null)
                {
                    foreach (var m in inPlace.Mappings)
                    {
                        if (!m.IsSettable)
                        {
                            spc.ReportDiagnostic(Diagnostic.Create(
                                Diagnostics.ZAMP012_UpdateInPlace_NotSettable,
                                decl.Location,
                                dst.ToDisplayString(),
                                m.TargetPropertyName));
                            break;
                        }
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

        // ZAMP013/014/015 — polymorphic dispatcher diagnostics.
        if (cls.PolymorphicDecls is not null)
        {
            foreach (var poly in cls.PolymorphicDecls)
            {
                var kindLabel = poly.Kind == MappingKind.Map ? "Map" : "TryMap";
                var baseDisplay = poly.BaseTypeSymbol.ToDisplayString();
                var baseDstDisplay = poly.BaseDestinationTypeSymbol.ToDisplayString();

                // ZAMP014 — sealed base.
                if (poly.BaseTypeSymbol.IsSealed)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP014_PolymorphicSealedBase,
                        poly.Location, kindLabel, baseDisplay, baseDstDisplay));
                }

                // Filter all decls assignable to the polymorphic base/destination.
                var assignable = new System.Collections.Generic.List<MappingDecl>();
                foreach (var decl in cls.Mappings)
                {
                    if (decl.SourceTypeSymbol is null || decl.DestinationTypeSymbol is null) continue;
                    if (!MapEmitter.IsAssignableTo(decl.SourceTypeSymbol, poly.BaseTypeSymbol)) continue;
                    if (!MapEmitter.IsAssignableTo(decl.DestinationTypeSymbol, poly.BaseDestinationTypeSymbol)) continue;
                    assignable.Add(decl);
                }

                var matchingKind = assignable.Where(d => d.Kind == poly.Kind).ToList();
                var mismatchKind = assignable.Where(d => d.Kind != poly.Kind).ToList();

                // ZAMP013 — no matching-kind cases.
                if (matchingKind.Count == 0)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP013_PolymorphicNoCases,
                        poly.Location, kindLabel, baseDisplay, baseDstDisplay));
                }

                // ZAMP015 — fires only when a (src,dst) pair has a wrong-kind decl with
                // NO matching-kind sibling for the same pair. A pair that has BOTH kinds
                // (intentional dual emission) does NOT fire — the dispatcher correctly
                // selects the matching-kind sibling.
                var wrongKindOnlyPair = mismatchKind
                    .GroupBy(m => (m.SourceTypeFqn, m.DestinationTypeFqn))
                    .Any(g => !matchingKind.Any(mk =>
                        mk.SourceTypeFqn == g.Key.SourceTypeFqn &&
                        mk.DestinationTypeFqn == g.Key.DestinationTypeFqn));

                if (wrongKindOnlyPair)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        Diagnostics.ZAMP015_PolymorphicMixedKinds,
                        poly.Location, kindLabel, baseDisplay, baseDstDisplay));
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
