; Shipped analyzer releases.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|--------------------------------------------------------------
ZAMP001 | ZeroAlloc.Mapping | Error    | Required destination property has no source
ZAMP002 | ZeroAlloc.Mapping | Error    | No conversion path between source and destination property
ZAMP003 | ZeroAlloc.Mapping | Error    | Ambiguous source property after [MapProperty] resolution
ZAMP004 | ZeroAlloc.Mapping | Error    | [Map] chain references a [TryMap]-only mapper
ZAMP005 | ZeroAlloc.Mapping | Warning  | [MapProperty] references a non-existent property name
ZAMP006 | ZeroAlloc.Mapping | Error    | [Map]/[TryMap] applied to non-static-partial class
ZAMP007 | ZeroAlloc.Mapping | Error    | Nullable source mapped to non-nullable destination under [Map]
ZAMP008 | ZeroAlloc.Mapping | Error    | Constructor selection is ambiguous
ZAMP009 | ZeroAlloc.Mapping | Error    | [ReverseMap] is not safely reversible
ZAMP010 | ZeroAlloc.Mapping | Error    | Source property is not consumed under strict source mapping
ZAMP011 | ZeroAlloc.Mapping | Error    | Case-insensitive matching produces ambiguous source

## Release 1.2.0

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-------------------------------------------------
ZAMP012 | ZeroAlloc.Mapping | Error    | Destination type cannot be updated in place
ZAMP013 | ZeroAlloc.Mapping | Error    | [PolymorphicMap] declared with no derived cases
ZAMP014 | ZeroAlloc.Mapping | Warning  | [PolymorphicMap] over a sealed type is degenerate
ZAMP015 | ZeroAlloc.Mapping | Error    | [PolymorphicMap] mixes [Map] and [TryMap] derived cases

## Release 1.3.0

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-----------------------------------
ZAMP016 | ZeroAlloc.Mapping | Warning  | Duplicate [MappingCulture] declarations

## Release 1.5.0

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|--------------------------------------------------------------------------
ZAMP017 | ZeroAlloc.Mapping | Error    | [Map(Projection = true)] uses a feature EF Core cannot translate
ZAMP018 | ZeroAlloc.Mapping | Error    | [Map(CycleSafe = true)] references a non-CycleSafe nested mapping
ZAMP019 | ZeroAlloc.Mapping | Error    | [Map(DeepClone = true)] reaches an uncloneable type
ZAMP020 | ZeroAlloc.Mapping | Error    | [Map(DeepClone = true)] walks a cyclic type graph without CycleSafe = true

## Release 1.6.0

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-----------------------------------------------------------------------------------------
ZAMP021 | ZeroAlloc.Mapping | Error    | [Map(DeepClone = true, CycleSafe = true)] reaches a primary-ctor-only type in a cycle

## Release 1.6.8

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-------------------------------------------------------------
ZAMP022 | ZeroAlloc.Mapping | Warning  | Nested mapper inside a containing type that is not partial
ZAMP023 | ZeroAlloc.Mapping | Warning  | Mapping cannot be generated for its source and destination types
ZAMP024 | ZeroAlloc.Mapping | Error    | Mapper name differs only in case from another mapper
