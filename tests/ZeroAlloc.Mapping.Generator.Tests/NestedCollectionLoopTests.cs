using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Generator.Tests;

/// <summary>
/// A collection member with a nested element mapping maps through a for-loop helper, not
/// <c>Enumerable.ToList(Enumerable.Select(...))</c>, in every mode and for every collection shape
/// the generator supports (#153).
/// </summary>
public class NestedCollectionLoopTests
{
    private static readonly string[] Shapes =
    {
        "List<{0}>", "{0}[]", "IList<{0}>", "IReadOnlyList<{0}>",
        "ICollection<{0}>", "IReadOnlyCollection<{0}>", "IEnumerable<{0}>",
    };

    public static TheoryData<string, string, string> ShapesAndModes()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var mode in new[] { "Map", "CycleSafe", "DeepClone", "DeepCloneCycleSafe", "UpdateInPlace" })
        {
            // Each shape into itself, and the ones that change the kind of collection.
            foreach (var shape in Shapes) data.Add(mode, shape, shape);
            data.Add(mode, "List<{0}>", "{0}[]");
            data.Add(mode, "{0}[]", "List<{0}>");
            data.Add(mode, "IEnumerable<{0}>", "{0}[]");
            data.Add(mode, "IReadOnlyCollection<{0}>", "IReadOnlyList<{0}>");
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ShapesAndModes))]
    public void NestedCollectionMember_CompilesCleanly_WithoutLinq(string mode, string from, string to)
    {
        var source = Source(mode, from, to);
        var run = TestHarness.RunGeneratorAndCompile(source);

        Assert.Empty(run.GeneratorDiagnostics);
        Assert.Empty(run.Errors);
        Assert.DoesNotContain(run.Warnings, d => d.Id.StartsWith("CS86", StringComparison.Ordinal));

        var output = TestHarness.RunGenerator(source);
        Assert.DoesNotContain("Enumerable.Select(src.Items", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable.ToList(global::System.Linq.Enumerable.Select", output, StringComparison.Ordinal);
        Assert.Contains("__MapTo", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryShape_EmitsOnlyTheHelpersItUses()
    {
        var source = """
            using ZeroAlloc.Mapping;
            using System.Collections.Generic;
            namespace App;
            public sealed record ItemSrc(int Id);
            public sealed record ItemDst(int Id);
            public sealed record Src(
                List<ItemSrc> AsList, ItemSrc[] AsArray, IList<ItemSrc> AsIList, IReadOnlyList<ItemSrc> AsReadOnlyList,
                ICollection<ItemSrc> AsCollection, IEnumerable<ItemSrc> AsEnumerable, List<ItemSrc> ListToArray,
                IEnumerable<ItemSrc> EnumerableToArray);
            public sealed record Dst(
                List<ItemDst> AsList, ItemDst[] AsArray, IList<ItemDst> AsIList, IReadOnlyList<ItemDst> AsReadOnlyList,
                ICollection<ItemDst> AsCollection, IEnumerable<ItemDst> AsEnumerable, ItemDst[] ListToArray,
                ItemDst[] EnumerableToArray);
            [Map<Src, Dst>]
            [Map<ItemSrc, ItemDst>]
            [SkipCollectionOverloads]
            public static partial class M { }
            """;
        GeneratorSnapshot.VerifyText(TestHarness.RunGenerator(source));
    }

    private static string Source(string mode, string from, string to)
    {
        var src = string.Format(System.Globalization.CultureInfo.InvariantCulture, from, "ItemSrc");
        var dst = string.Format(System.Globalization.CultureInfo.InvariantCulture, to, mode.StartsWith("DeepClone", StringComparison.Ordinal) ? "ItemSrc" : "ItemDst");
        var (attributes, partial) = mode switch
        {
            "Map" => ("[Map<Src, Dst>]\n[Map<ItemSrc, ItemDst>]", ""),
            "CycleSafe" => ("[Map<Src, Dst>(CycleSafe = true)]\n[Map<ItemSrc, ItemDst>(CycleSafe = true)]", ""),
            "DeepClone" => ("[Map<Src, Dst>(DeepClone = true)]", ""),
            "DeepCloneCycleSafe" => ("[Map<Src, Dst>(DeepClone = true, CycleSafe = true)]", ""),
            _ => ("[Map<Src, Dst>]\n[Map<ItemSrc, ItemDst>]", "public static partial void Map(Src src, Dst existingDst);"),
        };
        // [Map] builds the destination through its constructor; the other modes set properties.
        var types = mode == "Map"
            ? $"public sealed record Src({src} Items);\npublic sealed record Dst({dst} Items);"
            : $"public sealed class Src {{ public {src} Items {{ get; set; }} = null!; }}\n" +
              $"public sealed class Dst {{ public {dst} Items {{ get; set; }} = null!; }}";
        return $$"""
            #nullable enable
            using ZeroAlloc.Mapping;
            using System.Collections.Generic;
            namespace App;
            public sealed class ItemSrc { public int Id { get; set; } }
            public sealed class ItemDst { public int Id { get; set; } }
            {{types}}
            {{attributes}}
            public static partial class M { {{partial}} }
            """;
    }
}
