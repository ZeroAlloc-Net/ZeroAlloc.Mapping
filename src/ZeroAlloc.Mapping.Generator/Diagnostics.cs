using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

internal static class Diagnostics
{
    private const string Category = "ZeroAlloc.Mapping";

    public static readonly DiagnosticDescriptor ZAMP001_DestinationHasNoSource = new(
        id: "ZAMP001",
        title: "Required destination property has no source",
        messageFormat: "Required destination property '{0}' on '{1}' has no matching source property, [MapProperty] rename, or [MapValue] constant",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP002_NoConversionPath = new(
        id: "ZAMP002",
        title: "No conversion path between source and destination property",
        messageFormat: "Property '{0}' has no conversion path from '{1}' to '{2}' (no implicit/explicit cast, single-arg ctor, Parse, or nested mapper)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP003_AmbiguousSource = new(
        id: "ZAMP003",
        title: "Ambiguous source property after [MapProperty] resolution",
        messageFormat: "Two source properties match destination '{0}' after [MapProperty] resolution",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP004_MapChainsTryMap = new(
        id: "ZAMP004",
        title: "[Map] chain references a [TryMap]-only mapper",
        messageFormat: "[Map<{0}, {1}>] chains to a nested mapper that is only declared as [TryMap]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP005_MapPropertyTargetMissing = new(
        id: "ZAMP005",
        title: "[MapProperty] references a non-existent property name",
        messageFormat: "[MapProperty] references property name '{0}' that does not exist on '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP006_NotStaticPartialClass = new(
        id: "ZAMP006",
        title: "[Map]/[TryMap] applied to non-static-partial class",
        messageFormat: "[Map<,>] / [TryMap<,>] requires the host class to be 'static partial' — '{0}' is not",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP007_NullableMismatch = new(
        id: "ZAMP007",
        title: "Nullable source mapped to non-nullable destination under [Map]",
        messageFormat: "Property '{0}' has nullable source type but non-nullable destination type under [Map] — use [TryMap] or [MapValue] fallback",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP008_AmbiguousConstructor = new(
        id: "ZAMP008",
        title: "Constructor selection is ambiguous",
        messageFormat: "Destination type '{0}' has multiple constructors with equal preference — disambiguate via [MapProperty] / [MapperIgnoreSource]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZAMP011_CaseInsensitiveAmbiguous = new(
        id: "ZAMP011",
        title: "Case-insensitive matching produces ambiguous source",
        messageFormat: "Under [CaseInsensitiveMapping], two source properties collide on destination param '{0}' — disambiguate via [MapperIgnoreSource] or [MapProperty]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
