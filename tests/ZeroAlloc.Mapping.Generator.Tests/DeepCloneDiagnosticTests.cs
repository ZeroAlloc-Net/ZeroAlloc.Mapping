namespace ZeroAlloc.Mapping.Generator.Tests;

public class DeepCloneDiagnosticTests
{
    [Fact]
    public void ZAMP019_FiresWhen_DeepClone_Reaches_NonCloneable_Type()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed class Unreachable { private Unreachable() { } public string Name { get; } = ""; }
            public sealed class Src { public int Id { get; set; } public Unreachable? Lookup { get; set; } }
            public sealed class Dst { public int Id { get; set; } public Unreachable? Lookup { get; set; } }
            [Map<Src, Dst>(DeepClone = true)]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP019");
    }

    [Fact]
    public void ZAMP020_FiresWhen_DeepClone_Walks_CyclicTypeGraph_WithoutCycleSafe()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed class Node { public string Name { get; set; } = ""; public Node? Self { get; set; } }
            [Map<Node, Node>(DeepClone = true)]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.Contains(diags, d => d.Id == "ZAMP020");
    }

    [Fact]
    public void ZAMP020_DoesNotFire_When_DeepClone_With_CycleSafe()
    {
        var source = """
            using ZeroAlloc.Mapping;
            public sealed class Node { public string Name { get; set; } = ""; public Node? Self { get; set; } }
            [Map<Node, Node>(DeepClone = true, CycleSafe = true)]
            public static partial class M { }
            """;
        var diags = TestHarness.RunDiagnostics(source);
        Assert.DoesNotContain(diags, d => d.Id == "ZAMP020");
    }
}
