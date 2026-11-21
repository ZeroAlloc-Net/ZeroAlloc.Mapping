; Unshipped analyzer release.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### Changed Rules

Rule ID | New Category      | New Severity | Old Category      | Old Severity | Notes
--------|-------------------|--------------|-------------------|--------------|-----------------------------------------------------------------
ZAMP022 | ZeroAlloc.Mapping | Error        | ZeroAlloc.Mapping | Warning      | Nested mapper inside a containing type that is not partial
ZAMP023 | ZeroAlloc.Mapping | Error        | ZeroAlloc.Mapping | Warning      | Mapping cannot be generated for its source and destination types
