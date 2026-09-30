using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Mapping.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class MappingGenerator : IIncrementalGenerator
{
    /// <summary>
    /// The mapping attributes that make a type a mapper host, each paired with the name of the
    /// pipeline step that finds its hosts.
    /// </summary>
    private static readonly (string AttributeFqn, string TrackingName)[] HostAttributes =
    {
        (MapperDiscovery.MapAttributeFqn, "MapperHosts.Map"),
        (MapperDiscovery.TryMapAttributeFqn, "MapperHosts.TryMap"),
        (MapperDiscovery.ReverseMapAttributeFqn, "MapperHosts.ReverseMap"),
        (MapperDiscovery.ReverseTryMapAttributeFqn, "MapperHosts.ReverseTryMap"),
        (MapperDiscovery.PolymorphicMapAttributeFqn, "MapperHosts.PolymorphicMap"),
        (MapperDiscovery.PolymorphicTryMapAttributeFqn, "MapperHosts.PolymorphicTryMap"),
    };

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var providers = new System.Collections.Generic.List<IncrementalValuesProvider<MapperOutput>>();
        foreach (var (attributeFqn, trackingName) in HostAttributes)
        {
            providers.Add(context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    attributeFqn,
                    predicate: static (node, _) => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax,
                    transform: static (ctx, ct) => BuildOutput(ctx, ct))
                .Where(static o => o is not null)
                .Select(static (o, _) => o!)
                .WithTrackingName(trackingName));
        }

        // ZAMP024 — the one check that needs every host at once. Only the hint names that must
        // not be added reach the per-host outputs, so an edit that leaves them equal keeps every
        // other host's output cached.
        var allHosts = providers[0].Collect();
        for (var i = 1; i < providers.Count; i++)
        {
            allHosts = allHosts
                .Combine(providers[i].Collect())
                .Select(static (pair, _) => pair.Left.AddRange(pair.Right));
        }
        var collisions = allHosts
            .Select(static (hosts, _) => CaseCollisions.Find(hosts))
            .WithTrackingName("CaseCollisions");

        context.RegisterSourceOutput(collisions, static (spc, found) =>
        {
            foreach (var d in found.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());
        });

        var skippedHintNames = collisions.Select(static (found, _) => found.SkippedHintNames);
        foreach (var hosts in providers)
        {
            context.RegisterSourceOutput(
                hosts.Combine(skippedHintNames),
                static (spc, pair) =>
                {
                    var (output, skipped) = pair;
                    foreach (var d in output.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());
                    if (output.HintName is not null && output.Source is not null &&
                        !skipped.Values.Contains(output.HintName))
                    {
                        spc.AddSource(output.HintName, output.Source);
                    }
                });
        }
    }

    /// <summary>
    /// Does all of one host's semantic work and keeps only values: its source and diagnostics.
    /// </summary>
    /// <remarks>
    /// A host carrying several mapping attributes, or one spread over several partial
    /// declarations, is matched by more than one provider. Only the match holding its first
    /// mapping attribute builds the output, so each host is generated once.
    /// </remarks>
    private static MapperOutput? BuildOutput(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol type) return null;

        var comp = ctx.SemanticModel.Compilation;
        var attributes = new MapperDiscovery.AttributeSymbols(comp);
        if (!OwnsHost(ctx, type, attributes)) return null;

        ct.ThrowIfCancellationRequested();

        var diagnostics = new System.Collections.Generic.List<DiagnosticInfo>();

        // ZAMP006 — a mapping attribute on anything but a static partial class.
        if (!MapperDiscovery.IsStaticPartialClass(type))
        {
            var declaration = type.Locations.FirstOrDefault(static l => l.IsInSource) ?? ctx.TargetNode.GetLocation();
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.ZAMP006_NotStaticPartialClass,
                declaration,
                type.ToDisplayString()));
            return new MapperOutput(null, null, ToEquatable(diagnostics), null, null);
        }

        // ZAMP022 — the generated code reopens every containing type, so each must be partial.
        if (HostDeclarations.FirstNonPartialContainingType(type) is { } notPartial)
        {
            var declaration = type.Locations.FirstOrDefault(static l => l.IsInSource) ?? ctx.TargetNode.GetLocation();
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.ZAMP022_ContainingTypeNotPartial,
                declaration,
                type.ToDisplayString(),
                notPartial.ToDisplayString()));
            return new MapperOutput(null, null, ToEquatable(diagnostics), null, null);
        }

        var cls = MapperDiscovery.DiscoverHost(type, attributes, diagnostics);
        if (cls is null)
        {
            return diagnostics.Count == 0 ? null : new MapperOutput(null, null, ToEquatable(diagnostics), null, null);
        }

        ReportPerClassDiagnostics(diagnostics, cls, comp);
        var (src, mapEmitterDiagnostics) = MapEmitter.Emit(cls, comp);
        diagnostics.AddRange(mapEmitterDiagnostics);
        return new MapperOutput(
            HintNames.ForHost(type),
            src,
            ToEquatable(diagnostics),
            type.ToDisplayString(),
            LocationInfo.From(type.Locations.FirstOrDefault(static l => l.IsInSource) ?? ctx.TargetNode.GetLocation()));
    }

    private static bool OwnsHost(GeneratorAttributeSyntaxContext ctx, INamedTypeSymbol type, MapperDiscovery.AttributeSymbols attributes)
    {
        var first = type.GetAttributes().FirstOrDefault(attributes.IsMappingAttribute)?.ApplicationSyntaxReference;
        if (first is null) return false;
        foreach (var a in ctx.Attributes)
        {
            if (a.ApplicationSyntaxReference is { } syntax &&
                syntax.SyntaxTree == first.SyntaxTree &&
                syntax.Span == first.Span)
            {
                return true;
            }
        }
        return false;
    }

    private static EquatableArray<DiagnosticInfo> ToEquatable(System.Collections.Generic.List<DiagnosticInfo> diagnostics) =>
        new(System.Collections.Immutable.ImmutableArray.CreateRange(diagnostics));

    private static void ReportPerClassDiagnostics(System.Collections.Generic.List<DiagnosticInfo> diagnostics, MapperClass cls, Compilation comp)
    {
        // ZAMP016 — duplicate [MappingCulture] across partial parts.
        if (cls.Culture is not null && cls.TypeSymbol is not null)
        {
            // Discovery keeps the first [MappingCulture]; the diagnostic points at the second, the
            // one that is ignored.
            AttributeData? duplicate = null;
            var cultureCount = 0;
            foreach (var a in cls.TypeSymbol.GetAttributes())
            {
                if (a.AttributeClass is { Name: "MappingCultureAttribute" } ac &&
                    ac.ContainingNamespace is { Name: "Mapping", ContainingNamespace.Name: "ZeroAlloc" } &&
                    ++cultureCount == 2)
                {
                    duplicate = a;
                    break;
                }
            }
            if (duplicate is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.ZAMP016_DuplicateMappingCulture,
                    MapperDiscovery.AttributeLocation(duplicate, cls.TypeSymbol.Locations[0]),
                    cls.TypeSymbol.ToDisplayString(),
                    cls.Culture));
            }
        }

        foreach (var decl in cls.Mappings)
        {
            // ZAMP017 — [Map(Projection = true)] is incompatible with hooks,
            // culture, and polymorphic dispatch (EF Core's LINQ translator
            // cannot model them). Transitive note: when a nested [Map<,>] is
            // declared in the SAME MapperClass, the outer decl already shares
            // cls.Culture / cls.Hooks / cls.PolymorphicDecls and the direct
            // check below catches it. Cross-class transitive checking (when
            // the nested mapping lives in a *different* partial-class instance)
            // would require a cross-class registry of MapperClass metadata,
            // which the IncrementalGenerator pipeline does not currently
            // expose. Deferred to a follow-up commit.
            // ZAMP018 — [Map(CycleSafe = true)] requires every nested mapping reached
            // from the property graph to also be CycleSafe = true (transitive enforcement).
            // Cross-class lookup (the nested decl living in a different MapperClass) is
            // deferred — same caveat as ZAMP017's transitive note above.
            if (decl.CycleSafe)
            {
                foreach (var (nestedSrcFqn, nestedDstFqn) in WalkNestedMappingFqns(decl, cls))
                {
                    if (!IsCycleSafe(nestedSrcFqn, nestedDstFqn, cls))
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.ZAMP018_CycleSafeMissingOnNested,
                            decl.Location,
                            decl.SourceTypeFqn, decl.DestinationTypeFqn,
                            $"{nestedSrcFqn} -> {nestedDstFqn}"));
                    }
                }
            }

            // ZAMP019 / ZAMP020 — DeepClone walks the reachable-type graph at compile time.
            // ZAMP019 fires when a reached type cannot be cloned (no public ctor / cannot
            // satisfy the primary ctor from src.*). ZAMP020 fires when the type graph contains
            // a cycle and CycleSafe = false (CycleSafe + DeepClone resolves cycles at runtime
            // via the tracker — that combo is handled by EmitCycleSafeMapPair).
            // Symmetric with the ZAMP020 suppression inside the loop below: when
            // CycleSafe = true, MapEmitter routes through EmitCycleSafeMapPair and the
            // DeepCloneEmitter literal walk does NOT run. Reporting ZAMP019 uncloneable-
            // type diagnostics for a code path the generator never executes would be
            // misleading. The CycleSafe routing wins; deep-clone literal walks for the
            // CycleSafe + DeepClone combination are deferred (see docs/backlog.md B12).
            if (decl.DeepClone && !decl.CycleSafe)
            {
                var firedZamp019 = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                var firedZamp020 = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
                foreach (var rt in DeepCloneEmitter.WalkReachableReferenceTypes(decl, cls, comp))
                {
                    if (rt.IsCycle)
                    {
                        // decl.CycleSafe is always false here (outer guard skips
                        // the entire DeepClone walk when CycleSafe = true).
                        if (firedZamp020.Add(rt.DestinationFqn))
                        {
                            diagnostics.Add(DiagnosticInfo.Create(
                                Diagnostics.ZAMP020_DeepCloneCyclicTypeGraph,
                                decl.Location,
                                decl.SourceTypeFqn, decl.DestinationTypeFqn,
                                rt.DestinationFqn));
                        }
                        continue;
                    }

                    if (!DeepCloneEmitter.IsCloneable(rt.Destination, rt.Source) &&
                        firedZamp019.Add(rt.DestinationFqn))
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.ZAMP019_DeepCloneUncloneableType,
                            decl.Location,
                            decl.SourceTypeFqn, decl.DestinationTypeFqn,
                            rt.DestinationFqn));
                    }
                }
            }

            if (decl.Projection)
            {
                if (!string.IsNullOrEmpty(cls.Culture))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP017_ProjectionIncompatibleFeature,
                        decl.Location,
                        decl.SourceTypeFqn, decl.DestinationTypeFqn,
                        "[MappingCulture] is set on the enclosing class"));
                }
                if (cls.Hooks is not null && cls.Hooks.Count > 0)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP017_ProjectionIncompatibleFeature,
                        decl.Location,
                        decl.SourceTypeFqn, decl.DestinationTypeFqn,
                        "the enclosing class declares [BeforeMap] / [AfterMap] hooks"));
                }
                if (cls.PolymorphicDecls is not null && cls.PolymorphicDecls.Count > 0)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP017_ProjectionIncompatibleFeature,
                        decl.Location,
                        decl.SourceTypeFqn, decl.DestinationTypeFqn,
                        "[PolymorphicMap<,>] is declared on the enclosing class"));
                }
            }

            var src = decl.SourceTypeSymbol;
            var dst = decl.DestinationTypeSymbol;

            // ZAMP009 — [ReverseMap]/[ReverseTryMap] desugared decls cannot be auto-reversed
            // when the user-declared partial carries information-asymmetric customisations.
            if (decl.FromReverse && decl.UserPartialMethod is not null)
            {
                foreach (var attr in decl.UserPartialMethod.GetAttributes())
                {
                    var name = attr.AttributeClass?.Name;
                    if (name == "MapPropertyAttribute" || name == "MapValueAttribute" || name == "MapperIgnoreTargetAttribute")
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.ZAMP009_ReverseMapNotSymmetric,
                            MapperDiscovery.AttributeLocation(attr, decl.Location),
                            src.ToDisplayString(),
                            dst.ToDisplayString(),
                            "[" + name!.Replace("Attribute", "") + "]"));
                        break;
                    }
                }
            }

            // ZAMP023 — no public constructor to build the destination with, so MapEmitter
            // generates no method for this mapping and the checks below have nothing to check.
            var match = PropertyMatcher.Match(src, dst, decl.UserPartialMethod, cls.CaseInsensitive);
            if (match is null)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.ZAMP023_MappingNotGenerated,
                    decl.Location,
                    src.ToDisplayString(),
                    dst.ToDisplayString(),
                    $"'{dst.ToDisplayString()}' has no public constructor"));
                continue;
            }

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
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP011_CaseInsensitiveAmbiguous,
                        decl.Location,
                        matchingParam.Name));
                }
            }

            // ZAMP001 — unmatched required destination params (those without [MapValue] or source).
            // [TryMap] needs them as much as [Map]: the constructor call cannot be built without.
            foreach (var unmatched in match.UnmatchedTargetParams)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.ZAMP001_DestinationHasNoSource,
                    decl.Location,
                    unmatched, dst.ToDisplayString()));
            }

            // ZAMP002 — no conversion path on a matched pair.
            foreach (var m in match.Mappings)
            {
                if (ConversionResolver.HasNoConversionPath(m, cls, comp, out var conv))
                {
                    var from = m.SourceType.ToDisplayString();
                    var to = m.TargetType.ToDisplayString();
                    var reason = conv switch
                    {
                        { Collection: { ElementKind: ConversionKind.Explicit } c } =>
                            $"only an explicit conversion from element '{c.SourceElement.ToDisplayString()}' to '{c.TargetElement.ToDisplayString()}' exists, which can throw or lose data, and the generator applies implicit conversions only",
                        { Collection: { } c } =>
                            $"no implicit conversion from element '{c.SourceElement.ToDisplayString()}' to '{c.TargetElement.ToDisplayString()}', and no nested mapper for the elements",
                        { Kind: ConversionKind.Explicit } =>
                            $"only an explicit conversion from '{from}' to '{to}' exists, which can throw or lose data, and the generator applies implicit conversions only",
                        _ => "no implicit conversion, single-arg ctor, Parse, or nested mapper",
                    };
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP002_NoConversionPath,
                        decl.Location,
                        m.TargetParamName,
                        from,
                        to,
                        reason));
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
                        diagnostics.Add(DiagnosticInfo.Create(
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
                        diagnostics.Add(DiagnosticInfo.Create(
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
                                        diagnostics.Add(DiagnosticInfo.Create(
                                            Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                            MapperDiscovery.AttributeLocation(attr, decl.Location), segment, cursor.ToDisplayString()));
                                        break;
                                    }
                                    cursor = found.Type as INamedTypeSymbol;
                                }
                            }
                            else if (!sourceProps.Contains(srcName))
                            {
                                diagnostics.Add(DiagnosticInfo.Create(
                                    Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                    MapperDiscovery.AttributeLocation(attr, decl.Location), srcName, src.ToDisplayString()));
                            }
                        }
                        if (dstName is not null && !ctorParams.Contains(dstName))
                        {
                            diagnostics.Add(DiagnosticInfo.Create(
                                Diagnostics.ZAMP005_MapPropertyTargetMissing,
                                MapperDiscovery.AttributeLocation(attr, decl.Location), dstName, dst.ToDisplayString()));
                        }
                    }
                }
            }

            // ZAMP003 — ambiguous source after [MapProperty] (rename collides with by-name match).
            if (decl.UserPartialMethod is not null)
            {
                var renames = decl.UserPartialMethod.GetAttributes()
                    .Where(a => a.AttributeClass?.Name == "MapPropertyAttribute" && a.ConstructorArguments.Length == 2)
                    .Select(a => (Attribute: a, Source: a.ConstructorArguments[0].Value as string, Target: a.ConstructorArguments[1].Value as string))
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
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.ZAMP003_AmbiguousSource,
                            MapperDiscovery.AttributeLocation(rename.Attribute, decl.Location), target));
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
                    diagnostics.Add(DiagnosticInfo.Create(
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
                            diagnostics.Add(DiagnosticInfo.Create(
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
            var nonCopy = dst.InstanceConstructors
                .Where(c => c.DeclaredAccessibility == Accessibility.Public)
                .Where(c => !(c.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(c.Parameters[0].Type, dst)))
                .ToList();
            var maxArity = nonCopy.Select(c => c.Parameters.Length).DefaultIfEmpty(0).Max();
            if (nonCopy.Count(c => c.Parameters.Length == maxArity) > 1)
            {
                diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.ZAMP008_AmbiguousConstructor,
                    decl.Location, dst.ToDisplayString()));
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
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP014_PolymorphicSealedBase,
                        poly.Location, kindLabel, baseDisplay, baseDstDisplay));
                }

                // Filter all decls assignable to the polymorphic base/destination.
                var assignable = new System.Collections.Generic.List<MappingDecl>();
                foreach (var decl in cls.Mappings)
                {
                    if (!MapEmitter.IsAssignableTo(decl.SourceTypeSymbol, poly.BaseTypeSymbol)) continue;
                    if (!MapEmitter.IsAssignableTo(decl.DestinationTypeSymbol, poly.BaseDestinationTypeSymbol)) continue;
                    assignable.Add(decl);
                }

                var matchingKind = assignable.Where(d => d.Kind == poly.Kind).ToList();
                var mismatchKind = assignable.Where(d => d.Kind != poly.Kind).ToList();

                // ZAMP013 — no matching-kind cases.
                if (matchingKind.Count == 0)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
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
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.ZAMP015_PolymorphicMixedKinds,
                        poly.Location, kindLabel, baseDisplay, baseDstDisplay));
                }
            }
        }
    }

    /// <summary>
    /// Enumerates the (sourceFqn, destinationFqn) pairs of every nested mapping reached
    /// from <paramref name="decl"/> via its declared property graph — used by ZAMP018
    /// (CycleSafe transitivity) to verify each hop also opts in to the tracker.
    /// </summary>
    private static System.Collections.Generic.IEnumerable<(string SrcFqn, string DstFqn)>
        WalkNestedMappingFqns(MappingDecl decl, MapperClass cls)
    {
        // No match means no public constructor, which ReportPerClassDiagnostics reports as ZAMP023.
        var match = PropertyMatcher.Match(decl.SourceTypeSymbol, decl.DestinationTypeSymbol, decl.UserPartialMethod, cls.CaseInsensitive);
        if (match is null) yield break;

        foreach (var m in match.Mappings)
        {
            // Direct nested object mapping.
            var nested = NestedMappingResolver.FindNestedMapper(cls, m.SourceType, m.TargetType);
            if (nested is not null)
            {
                yield return (
                    m.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    m.TargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                continue;
            }

            // Collection of nested mappings.
            var srcColl = NestedMappingResolver.AsCollection(m.SourceType);
            var dstColl = NestedMappingResolver.AsCollection(m.TargetType);
            if (srcColl is not null && dstColl is not null)
            {
                var nestedElem = NestedMappingResolver.FindNestedMapper(cls, srcColl.Value.Element, dstColl.Value.Element);
                if (nestedElem is not null)
                {
                    yield return (
                        srcColl.Value.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        dstColl.Value.Element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }
    }

    private static bool IsCycleSafe(string srcFqn, string dstFqn, MapperClass cls)
    {
        foreach (var d in cls.Mappings)
        {
            if (d.SourceTypeFqn == srcFqn && d.DestinationTypeFqn == dstFqn && d.CycleSafe)
                return true;
        }
        return false;
    }
}
