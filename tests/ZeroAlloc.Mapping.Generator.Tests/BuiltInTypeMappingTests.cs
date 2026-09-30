using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A mapping between two built-in types is a cast, not a mapping. It is reported as ZAMP023 and
/// nothing is generated for it (#144). It used to be matched like any class, so
/// <c>[Map&lt;int, long&gt;]</c> compiled to <c>new long()</c> and silently returned 0.
/// </summary>
public class BuiltInTypeMappingTests
{
    [Theory]
    [InlineData("Map<int, long>", "int", "long")]
    [InlineData("TryMap<int, string>", "int", "string")]
    [InlineData("Map<decimal, double>", "decimal", "double")]
    [InlineData("Map<string, object>", "string", "object")]
    [InlineData("Map<int?, long?>", "int?", "long?")]
    [InlineData("Map<bool, char>", "bool", "char")]
    [InlineData("ReverseMap<int, long>", "int", "long")]
    [InlineData("ReverseTryMap<byte, short>", "byte", "short")]
    [InlineData("PolymorphicMap<object, string>", "object", "string")]
    public void MappingBetweenBuiltInTypes_ReportsZamp023_AndGeneratesNothing(string attribute, string from, string to)
    {
        var source = $$"""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                [{{attribute}}]
                [Map<A, B>]
                public static partial class M { }
                public static class Calls
                {
                    public static int Run() => M.Map(new A(1)).X;
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);
        var output = TestHarness.RunGenerator(source);

        var zamp023 = Assert.Single(run.GeneratorDiagnostics);
        Assert.Equal("ZAMP023", zamp023.Id);
        Assert.Equal(
            $"The mapping from '{from}' to '{to}' is not generated because both are built-in types, and a conversion between them is a cast, not a mapping",
            zamp023.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var span = zamp023.Location.SourceSpan;
        Assert.Equal(attribute, source.Substring(span.Start, span.Length));
        Assert.DoesNotContain($"({from} src)", output, StringComparison.Ordinal);
        Assert.DoesNotContain($"({to} src)", output, StringComparison.Ordinal);
        Assert.Empty(run.Errors);
    }

    /// <summary>
    /// A built-in type on one side only is an ordinary mapping: its properties or constructor
    /// parameters are matched against the other type's.
    /// </summary>
    [Theory]
    [InlineData("Map<System.DateTime, App.Ymd>")]
    [InlineData("TryMap<decimal, App.Scaled>")]
    [InlineData("Map<App.Ymd, App.Ymd2>")]
    public void MappingWithAtMostOneBuiltInType_IsGenerated(string attribute)
    {
        var run = TestHarness.RunGeneratorAndCompile($$"""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record Ymd(int Year, int Month);
                public sealed record Ymd2(int Year, int Month);
                public sealed record Scaled(byte Scale);
                [{{attribute}}]
                public static partial class M { }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Single(run.HintNames);
        Assert.Empty(run.Errors);
    }
}
