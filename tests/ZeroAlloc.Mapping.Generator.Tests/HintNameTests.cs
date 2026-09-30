namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A generated file is named after its host's namespace and containing types, so two hosts
/// with the same simple name never produce the same hint name. A duplicate hint name made the
/// generator throw, and then no mapper in the project was generated (#131).
/// </summary>
public class HintNameTests
{
    [Fact]
    public void SameNamedHosts_InDifferentNamespaces_AreBothGenerated()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace N1 { public sealed record A(int X); public sealed record B(int X); [Map<A, B>] public static partial class M { } }
            namespace N2 { public sealed record A(int X); public sealed record B(int X); [Map<A, B>] public static partial class M { } }
            namespace Use
            {
                public static class Calls
                {
                    public static int Run() => N1.M.Map(new N1.A(1)).X + N2.M.Map(new N2.A(2)).X;
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(new[] { "N1.M.g.cs", "N2.M.g.cs" }, run.HintNames.Order(StringComparer.Ordinal));
        Assert.Empty(run.Errors);
    }

    [Fact]
    public void SameNamedHosts_InDifferentContainingTypes_AreBothGenerated()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App;
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
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(
            new[] { "App.Customers+Inner+Mappers.g.cs", "App.Orders+Mappers.g.cs" },
            run.HintNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NestedHost_IsNamedApartFrom_TopLevelHostInNamespaceOfTheSameName()
    {
        // A host nested in type App.Outer and a host at the top of namespace App.Outer would
        // both be "App.Outer.M" if nesting were written with a dot. C# forbids a namespace and
        // a type with one name in one compilation, so each is generated on its own here.
        var nested = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App;
            public sealed record A(int X);
            public sealed record B(int X);
            public static partial class Outer { [Map<A, B>] public static partial class M { } }
            """);
        var topLevel = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace App.Outer;
            public sealed record A(int X);
            public sealed record B(int X);
            [Map<A, B>] public static partial class M { }
            """);

        Assert.Equal("App.Outer+M.g.cs", Assert.Single(nested.HintNames));
        Assert.Equal("App.Outer.M.g.cs", Assert.Single(topLevel.HintNames));
    }

    [Fact]
    public void HostsInGlobalNamespace_AreNamedWithoutANamespacePrefix()
    {
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            public sealed record A(int X);
            public sealed record B(int X);
            public sealed record C(int Y);
            public sealed record D(int Y);
            [Map<A, B>] public static partial class M { }
            public static partial class Outer { [Map<C, D>] public static partial class M { } }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(new[] { "M.g.cs", "Outer+M.g.cs" }, run.HintNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void GenericHosts_AreNamedWithTheirArity()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace App
            {
                public sealed record A(int X);
                public sealed record B(int X);
                [Map<A, B>] public static partial class M { }
                [Map<A, B>] public static partial class M<T> { }
                [Map<A, B>] public static partial class M<T1, T2> { }
                public partial class Outer<T>
                {
                    [Map<A, B>] public static partial class M { }
                }
                public partial struct Holder<T> where T : class
                {
                    [Map<A, B>] public static partial class M<U> { }
                }
            }
            """;

        var run = TestHarness.RunGeneratorAndCompile(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal(
            new[] { "App.Holder`1+M`1.g.cs", "App.M.g.cs", "App.M`1.g.cs", "App.M`2.g.cs", "App.Outer`1+M.g.cs" },
            run.HintNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void VerbatimAndNonAsciiNames_AreNamedByTheirIdentifier()
    {
        // A verbatim identifier is named without its '@', and a letter outside ASCII is kept.
        var run = TestHarness.RunGeneratorAndCompile("""
            using ZeroAlloc.Mapping;
            namespace Café.@event;
            public sealed record A(int X);
            public sealed record B(int X);
            [Map<A, B>] public static partial class Ωmega { }
            """);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Equal("Café.event.Ωmega.g.cs", Assert.Single(run.HintNames));
    }

    /// <summary>
    /// A C# identifier cannot hold a character that is invalid in a hint name, and the
    /// compiler drops formatting characters from it, so escaping guards names the generator
    /// is handed rather than names users write. The escape starts with '-', which no
    /// identifier contains, so an escaped name cannot collide with one that needed none.
    /// </summary>
    [Theory]
    [InlineData("M", "M")]
    [InlineData("App.M", "App.M")]
    [InlineData("App.Outer+M`1", "App.Outer+M`1")]
    [InlineData("Café.Ωmega_1", "Café.Ωmega_1")]
    [InlineData("a/b|c:d*e?f<g>h", "a-u002Fb-u007Cc-u003Ad-u002Ae-u003Ff-u003Cg-u003Eh")]
    [InlineData("a-b", "a-u002Db")]
    public void Sanitize_KeepsIdentifierCharactersAndEscapesTheRest(string name, string expected)
    {
        Assert.Equal(expected, HintNames.Sanitize(name));
    }

    [Fact]
    public void Sanitize_EscapesControlAndSeparatorCharacters_AndKeepsAstralLetters()
    {
        var backslash = ((char)92).ToString();
        var quote = ((char)34).ToString();
        var tab = ((char)9).ToString();
        var mathBoldA = char.ConvertFromUtf32(0x1D400);

        Assert.Equal("a-u005Cb", HintNames.Sanitize("a" + backslash + "b"));
        Assert.Equal("a-u0022b", HintNames.Sanitize("a" + quote + "b"));
        Assert.Equal("a-u0009b", HintNames.Sanitize("a" + tab + "b"));
        Assert.Equal(mathBoldA + "x", HintNames.Sanitize(mathBoldA + "x"));
        Assert.Equal("-uD835x", HintNames.Sanitize(mathBoldA.Substring(0, 1) + "x"));
    }
}
