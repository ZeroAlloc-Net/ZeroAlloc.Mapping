
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class TryMapEmissionTests
{
    [Fact]
    public void TryMap_Flat_Emits_ResultReturning_Method()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [TryMap<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    [Fact]
    public void TryMap_With_SingleArgCtor_Wraps_In_TryCatch()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Email
            {
                public Email(string value)
                {
                    if (string.IsNullOrEmpty(value)) throw new System.ArgumentException("empty");
                    Value = value;
                }
                public string Value { get; }
            }
            public sealed record Src(string Email);
            public sealed record Dst(Email Email);
            [TryMap<Src, Dst>]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }
}
