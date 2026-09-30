using System.Collections.Generic;
using System.Collections.Immutable;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// The hosts whose hint names differ only in case from an earlier host's, which Roslyn would
/// reject as duplicates, and the ZAMP024 errors about them.
/// </summary>
internal sealed record CaseCollisions(
    EquatableArray<string> SkippedHintNames,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    /// <summary>
    /// Groups the generated hosts by hint name as Roslyn compares them, ignoring case. In each
    /// group the host declared first, by file path and then position, keeps its file; every
    /// later host is skipped and reported. The order does not depend on which attribute found
    /// a host, so the same host is generated on every run.
    /// </summary>
    public static CaseCollisions Find(ImmutableArray<MapperOutput> hosts)
    {
        var generated = new List<MapperOutput>();
        foreach (var host in hosts)
        {
            if (host.HintName is not null && host.Source is not null) generated.Add(host);
        }
        generated.Sort(CompareDeclarationOrder);

        var first = new Dictionary<string, MapperOutput>(System.StringComparer.OrdinalIgnoreCase);
        var skipped = ImmutableArray.CreateBuilder<string>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
        foreach (var host in generated)
        {
            if (!first.TryGetValue(host.HintName!, out var earlier))
            {
                first.Add(host.HintName!, host);
                continue;
            }

            skipped.Add(host.HintName!);
            diagnostics.Add(new DiagnosticInfo(
                ZeroAlloc.Mapping.Generator.Diagnostics.ZAMP024_HostNameDiffersOnlyInCase,
                host.HostLocation,
                new EquatableArray<string>(ImmutableArray.Create(
                    host.HostName ?? string.Empty,
                    host.HintName!,
                    earlier.HostName ?? string.Empty))));
        }

        return new CaseCollisions(
            new EquatableArray<string>(skipped.ToImmutable()),
            new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutable()));
    }

    private static int CompareDeclarationOrder(MapperOutput x, MapperOutput y)
    {
        var byPath = string.CompareOrdinal(x.HostLocation?.Tree.FilePath, y.HostLocation?.Tree.FilePath);
        if (byPath != 0) return byPath;
        var byPosition = (x.HostLocation?.Span.Start ?? 0).CompareTo(y.HostLocation?.Span.Start ?? 0);
        return byPosition != 0 ? byPosition : string.CompareOrdinal(x.HintName, y.HintName);
    }
}
