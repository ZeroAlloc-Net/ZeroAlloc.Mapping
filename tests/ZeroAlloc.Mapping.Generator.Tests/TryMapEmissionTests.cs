using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class TryMapEmissionTests
{
    [Fact]
    public Task TryMap_Flat_Emits_ResultReturning_Method()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed record Src(int Id);
            public sealed record Dst(int Id);
            [TryMap<Src, Dst>]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }

    [Fact]
    public Task TryMap_With_SingleArgCtor_Wraps_In_TryCatch()
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
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
