
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class ConversionTests
{
    [Fact]
    public void Conversion_StringToInt_Uses_Parse_Invariant()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(string Quantity);
            public sealed record Dst(int Quantity);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void Conversion_IntToValueObject_Uses_SingleArgCtor()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public readonly record struct OrderId(int Value);
            public sealed record Src(int Id);
            public sealed record Dst(OrderId Id);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void Conversion_StringToEnum_Uses_EnumParse()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public enum Color { Red, Green, Blue }
            public sealed record Src(string C);
            public sealed record Dst(Color C);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void Conversion_IntToLong_Uses_ImplicitCast()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int X);
            public sealed record Dst(long X);
            [Map<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }
}
