using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace ZeroAlloc.Mapping.Generator;

/// <summary>
/// A diagnostic to report, held as values so the incremental pipeline can compare it between
/// runs. A <see cref="Diagnostic"/> is not safe to cache: its arguments are arbitrary objects.
/// </summary>
internal sealed record DiagnosticInfo(
    DiagnosticDescriptor Descriptor,
    LocationInfo? Location,
    EquatableArray<string> MessageArgs)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location location, params string?[] messageArgs)
    {
        var args = ImmutableArray.CreateBuilder<string>(messageArgs.Length);
        // A null argument formats as an empty string, so storing it as one keeps the message.
        foreach (var arg in messageArgs) args.Add(arg ?? string.Empty);
        return new DiagnosticInfo(descriptor, LocationInfo.From(location), new EquatableArray<string>(args.MoveToImmutable()));
    }

    public Diagnostic ToDiagnostic()
    {
        var args = new object[MessageArgs.Length];
        for (var i = 0; i < args.Length; i++) args[i] = MessageArgs[i];
        return Diagnostic.Create(Descriptor, Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, args);
    }
}

/// <summary>
/// A source location as its syntax tree and span. Rebuilding a <see cref="Location"/> from the
/// tree keeps it in source, so <c>#pragma warning disable</c> still applies to the diagnostic.
/// A tree is replaced only when its own file is edited, so an edit elsewhere keeps it equal.
/// </summary>
internal sealed record LocationInfo(SyntaxTree Tree, TextSpan Span)
{
    public Location ToLocation() => Microsoft.CodeAnalysis.Location.Create(Tree, Span);

    public static LocationInfo? From(Location location) =>
        location.SourceTree is { } tree ? new LocationInfo(tree, location.SourceSpan) : null;
}

/// <summary>
/// What the generator produces for one mapper host: its source, when it has one, and the
/// diagnostics about it. Holds no symbols, syntax nodes or compilation, so it compares by value.
/// </summary>
internal sealed record MapperOutput(
    string? HintName,
    string? Source,
    EquatableArray<DiagnosticInfo> Diagnostics);
