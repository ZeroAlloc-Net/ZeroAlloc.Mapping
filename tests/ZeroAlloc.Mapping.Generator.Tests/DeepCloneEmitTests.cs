using VerifyXunit;

namespace ZeroAlloc.Mapping.Generator.Tests;

public class DeepCloneEmitTests
{
    [Fact]
    public Task DeepClone_Flat()
    {
        var source = """
            using ZeroAlloc.Mapping;
            namespace MyApp;
            public sealed class Tag { public string Name { get; set; } = ""; public int Id { get; set; } }
            public sealed class Src { public int OrderId { get; set; } public System.Collections.Generic.List<Tag>? Tags { get; set; } }
            public sealed class Dst { public int OrderId { get; set; } public System.Collections.Generic.List<Tag>? Tags { get; set; } }
            [Map<Src, Dst>(DeepClone = true)]
            public static partial class M { }
            """;
        return Verifier.Verify(TestHarness.RunGenerator(source)).UseDirectory("Snapshots");
    }
}
