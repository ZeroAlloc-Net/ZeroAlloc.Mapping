; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category          | Severity | Notes
--------|-------------------|----------|-------------------------------------------------------------
ZAMP022 | ZeroAlloc.Mapping | Warning  | Nested mapper inside a containing type that is not partial
ZAMP023 | ZeroAlloc.Mapping | Warning  | Mapping cannot be generated for its source and destination types
ZAMP024 | ZeroAlloc.Mapping | Error    | Mapper name differs only in case from another mapper
