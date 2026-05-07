namespace ZeroAlloc.Mapping;

/// <summary>
/// Structured failure shape for <see cref="TryMapAttribute{TSource, TDestination}"/>-decorated mappings.
/// </summary>
/// <param name="Code">Structured error code (e.g. <c>"mapping.constructor.threw"</c>, <c>"mapping.source.null"</c>,
/// <c>"mapping.collection.elements_failed"</c>). Open-ended; consumers and generators may mint new codes.</param>
/// <param name="PropertyPath">Dotted/indexed path to the failing property (e.g. <c>"Customer.Email"</c>,
/// <c>"Items[5].Quantity"</c>, <c>"(root)"</c> when the source itself is null).</param>
/// <param name="Reason">Human-readable detail. Typically captured from an inner <see cref="System.Exception"/>'s
/// <see cref="System.Exception.Message"/>.</param>
/// <param name="Children">Nested errors for collection-element failures or aggregated-detail nested-mapper failures.
/// <c>null</c> for single-error failures.</param>
public readonly record struct MappingError(
    string Code,
    string PropertyPath,
    string? Reason = null,
    System.Collections.Generic.IReadOnlyList<MappingError>? Children = null);
