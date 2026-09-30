using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A collection member whose elements have no nested mapping maps when its elements convert
/// implicitly, and is reported as ZAMP002, naming the element conversion, when they do not.
/// Every collection member used to count as having a path, so it passed ZAMP002 and was
/// emitted as is, and the build failed with CS1503 (#143).
/// </summary>
public class CollectionConversionTests
{
    public static TheoryData<string, string, string> Convertible() => new()
    {
        { "Map", "List<int>", "List<long>" },
        { "Map", "int[]", "List<long>" },
        { "Map", "List<int>", "long[]" },
        { "Map", "IEnumerable<int>", "IReadOnlyList<long>" },
        { "Map", "List<int>", "int[]" },
        { "Map", "List<Derived>", "List<Base>" },
        { "Map", "List<int>?", "List<long>?" },
        { "Map", "List<int?>", "List<long?>" },
        { "TryMap", "List<int>", "List<long>" },
        { "TryMap", "int[]", "long[]" },
        { "TryMap", "List<int>", "int[]" },
        { "TryMap", "List<int>?", "long[]?" },
    };

    [Theory]
    [MemberData(nameof(Convertible))]
    public void ImplicitElementConversion_Maps(string attribute, string from, string to)
    {
        var run = TestHarness.RunGeneratorAndCompile(Source(attribute, from, to));

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
        Assert.DoesNotContain(run.Warnings, d => d.Id.StartsWith("CS86", StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string, string> NotConvertible() => new()
    {
        { "Map", "List<long>", "List<int>", "only an explicit conversion from element 'long' to 'int' exists" },
        { "Map", "List<Base>", "Derived[]", "only an explicit conversion from element 'App.Base' to 'App.Derived' exists" },
        { "Map", "List<string>", "List<System.Guid>", "no implicit conversion from element 'string' to 'System.Guid'" },
        { "TryMap", "List<long>", "List<int>", "only an explicit conversion from element 'long' to 'int' exists" },
        { "TryMap", "List<string>", "List<System.Guid>", "no implicit conversion from element 'string' to 'System.Guid'" },
    };

    [Theory]
    [MemberData(nameof(NotConvertible))]
    public void ElementsWithoutImplicitConversion_ReportZamp002_NamingTheElements(
        string attribute, string from, string to, string reason)
    {
        var source = Source(attribute, from, to);
        var run = TestHarness.RunGeneratorAndCompile(source);

        var zamp002 = Assert.Single(run.GeneratorDiagnostics, d => d.Id == "ZAMP002");
        var message = zamp002.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.StartsWith("Property 'V' has no conversion path from '", message, StringComparison.Ordinal);
        Assert.Contains(reason, message, StringComparison.Ordinal);
        var span = zamp002.Location.SourceSpan;
        Assert.Equal($"{attribute}<Src, Dst>", source.Substring(span.Start, span.Length));
        Assert.DoesNotContain(run.Errors, d => d.Id == "CS1503");
    }

    /// <summary>
    /// A collection mapped to a type that is not a collection has no path either.
    /// </summary>
    [Theory]
    [InlineData("Map")]
    [InlineData("TryMap")]
    public void CollectionToNonCollection_ReportsZamp002(string attribute)
    {
        var run = TestHarness.RunGeneratorAndCompile(Source(attribute, "List<int>", "int"));

        var zamp002 = Assert.Single(run.GeneratorDiagnostics, d => d.Id == "ZAMP002");
        Assert.Contains(
            "no implicit conversion, single-arg ctor, Parse, or nested mapper",
            zamp002.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.DoesNotContain(run.Errors, d => d.Id == "CS1503");
    }

    /// <summary>
    /// A projection converts the elements inside the expression tree, where a static lambda
    /// is not allowed.
    /// </summary>
    [Fact]
    public void ImplicitElementConversion_InAProjection_Compiles()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            #nullable enable
            using ZeroAlloc.Mapping;
            using System.Collections.Generic;
            namespace App
            {
                public sealed record Src(List<int> V);
                public sealed record Dst(List<long> V);
                [Map<Src, Dst>(Projection = true)]
                public static partial class M { }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
    }

    private static string Source(string attribute, string from, string to) => $$"""
        #nullable enable
        using ZeroAlloc.Mapping;
        using System.Collections.Generic;
        namespace App
        {
            public class Base { }
            public sealed class Derived : Base { }
            public sealed record Src({{from}} V, string Ok);
            public sealed record Dst({{to}} V, string Ok);
            [{{attribute}}<Src, Dst>]
            public static partial class M { }
        }
        """;
}
