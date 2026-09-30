using Microsoft.CodeAnalysis;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A nested or generic host is generated into the host itself, inside partial declarations of
/// its containing types, and not into a new top-level class with its simple name (#134). A
/// host inside a containing type that is not partial gets ZAMP022 and no generated code.
/// </summary>
public class NestedHostTests
{
    [Fact]
    public void NestedHosts_WithTheSameName_AreGeneratedIntoTheirOwnHosts()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public sealed record C(int Y);
                public sealed record D(int Y);
                public static partial class Orders
                {
                    [Map<A, B>] public static partial class Mappers { }
                }
                public partial class Customers
                {
                    public partial record Inner
                    {
                        [Map<C, D>] public static partial class Mappers { }
                    }
                }
                public static class Calls
                {
                    public static int Run() =>
                        Orders.Mappers.Map(new A(1)).X + Customers.Inner.Mappers.Map(new C(2)).Y;
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(2, run.HintNames.Count);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void GenericHosts_AndHostsInGenericContainers_AreGeneratedIntoTheirOwnHosts()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                [Map<A, B>] public static partial class M { }
                [Map<A, B>] public static partial class M<T> { }
                [Map<A, B>] public static partial class M<T1, T2> where T1 : struct where T2 : class { }
                public partial class Outer<T>
                {
                    [Map<A, B>] public static partial class M { }
                }
                public static class Calls
                {
                    public static int Run() =>
                        M.Map(new A(1)).X + M<int>.Map(new A(2)).X + M<int, string>.Map(new A(3)).X
                        + Outer<int>.M.Map(new A(4)).X;
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(4, run.HintNames.Count);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void HostsInEveryKindOfContainingType_AreGeneratedIntoTheirOwnHosts()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public partial struct Holder<T> where T : class, new()
                {
                    [Map<A, B>] public static partial class M<U> where U : struct { }
                }
                public partial interface IHolder<T>
                {
                    [Map<A, B>] public static partial class M { }
                }
                public partial record struct Pair
                {
                    [Map<A, B>] public static partial class M { }
                }
                public readonly ref partial struct Span
                {
                    [Map<A, B>] public static partial class M { }
                }
                public sealed partial record Rec(int Z)
                {
                    [Map<A, B>] public static partial class M { }
                }
                public static class Calls
                {
                    public static int Run() =>
                        Holder<object>.M<int>.Map(new A(1)).X + IHolder<string>.M.Map(new A(2)).X
                        + Pair.M.Map(new A(3)).X + Span.M.Map(new A(4)).X + Rec.M.Map(new A(5)).X;
                }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(5, run.HintNames.Count);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void VerbatimNamedHostAndContainers_AreGeneratedIntoTheirOwnHosts()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public static partial class @class<@int>
                {
                    [Map<A, B>] public static partial class @static { }
                }
                public static class Calls { public static int Run() => @class<long>.@static.Map(new A(1)).X; }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void NestedGenericHost_EmitsThePartialDeclarationsOfItsContainingTypes()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App;
            public sealed record A(int X);
            public sealed record B(int X);
            public partial struct Holder<T>
            {
                public static partial class Mappers<U>
                {
                    [Map<A, B>] public static partial class Inner { }
                }
            }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void HostInANonPartialContainingType_ReportsZamp022_AndGeneratesNothing()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public class Outer
                {
                    [Map<A, B>] public static partial class Mappers { }
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);
        var diagnostics = TestHarness.RunDiagnostics(source);

        var zamp022 = Assert.Single(diagnostics);
        Assert.Equal("ZAMP022", zamp022.Id);
        Assert.Equal(DiagnosticSeverity.Warning, zamp022.Severity);
        Assert.Equal(
            "Mapper 'App.Outer.Mappers' is not generated because its containing type 'App.Outer' is not partial",
            zamp022.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        var span = zamp022.Location.SourceSpan;
        Assert.Equal("Mappers", source.Substring(span.Start, span.Length));
        Assert.Empty(run.HintNames);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void HostWhoseOuterContainingTypeIsNotPartial_ReportsZamp022_NamingThatType()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public static class Outer
                {
                    public partial class Middle
                    {
                        [Map<A, B>] public static partial class Mappers { }
                    }
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);
        var zamp022 = Assert.Single(TestHarness.RunDiagnostics(source));

        Assert.Equal("ZAMP022", zamp022.Id);
        Assert.Contains(
            "containing type 'App.Outer' is not partial",
            zamp022.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
        Assert.Empty(run.HintNames);
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void ContainingTypeDeclaredInSeveralParts_IsPartial()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                public partial class Outer { }
                public partial class Outer
                {
                    [Map<A, B>] public static partial class Mappers { }
                }
                public static class Calls { public static int Run() => Outer.Mappers.Map(new A(1)).X; }
            }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Single(run.HintNames);
        Assert.Empty(run.Errors);
    }
}
