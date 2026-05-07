using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ConversionTests
{
    [Fact]
    public Task Conversion_StringToInt_Uses_Parse_Invariant()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Conversion_IntToValueObject_Uses_SingleArgCtor()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public readonly record struct OrderId(int Value);
            public sealed record Src(int Id);
            public sealed record Dst(OrderId Id);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Conversion_StringToEnum_Uses_EnumParse()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public enum Color { Red, Green, Blue }
            public sealed record Src(string C);
            public sealed record Dst(Color C);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task Conversion_IntToLong_Uses_ImplicitCast()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(long X);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
