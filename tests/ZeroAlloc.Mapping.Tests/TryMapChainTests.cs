namespace ZeroAlloc.Mapping.Tests
{
    using TryMapChain;

    /// <summary>
    /// [TryMap] chains to the nested mappings its class declares, and propagates a nested
    /// failure with the member's path prefixed. It also maps a value-type source (#140). It used
    /// to emit code that did not compile for both.
    /// </summary>
    public class TryMapChainTests
    {
        [Fact]
        public void NestedTryMap_Success_MapsTheMember()
        {
            var result = ChainMappings.TryMap(new Outer(1, new Inner("a@b")));

            Assert.True(result.IsSuccess);
            Assert.Equal(1, result.Value.Id);
            Assert.Equal("a@b", result.Value.Child.Value.Text);
        }

        [Fact]
        public void NestedTryMap_Failure_PrefixesTheMemberPath()
        {
            var result = ChainMappings.TryMap(new Outer(1, new Inner("")));

            Assert.False(result.IsSuccess);
            Assert.Equal("mapping.constructor.threw", result.Error.Code);
            Assert.Equal("Child", result.Error.PropertyPath);
        }

        [Fact]
        public void NestedTryMap_NullMember_FailsAtTheMemberPath()
        {
            var result = ChainMappings.TryMap(new Outer(1, null!));

            Assert.False(result.IsSuccess);
            Assert.Equal("mapping.source.null", result.Error.Code);
            Assert.Equal("Child", result.Error.PropertyPath);
        }

        [Fact]
        public void NestedTryMap_NullableMember_PassesNullThrough()
        {
            var empty = ChainMappings.TryMap(new Optional(null));
            var set = ChainMappings.TryMap(new Optional(new Inner("x")));

            Assert.True(empty.IsSuccess);
            Assert.Null(empty.Value.Child);
            Assert.True(set.IsSuccess);
            Assert.Equal("x", set.Value.Child!.Value.Text);
        }

        [Fact]
        public void NestedTryMap_Collection_MapsEveryElement()
        {
            var result = ChainMappings.TryMap(new Batch(new List<Inner> { new("a"), new("b") }));

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "a", "b" }, result.Value.Items.Select(i => i.Value.Text));
        }

        [Fact]
        public void NestedTryMap_Collection_ReportsEveryFailedElement()
        {
            var result = ChainMappings.TryMap(new Batch(new List<Inner> { new(""), new("b"), new("") }));

            Assert.False(result.IsSuccess);
            Assert.Equal("mapping.collection.elements_failed", result.Error.Code);
            Assert.Equal("Items", result.Error.PropertyPath);
            Assert.Equal("2 of 3 elements failed", result.Error.Reason);
            Assert.Equal(new[] { "Items[0]", "Items[2]" }, result.Error.Children!.Select(c => c.PropertyPath));
        }

        [Fact]
        public void NestedMap_UnderTryMap_MapsTheMember()
        {
            var result = ChainMappings.TryMap(new Mixed(new Plain(5)));

            Assert.True(result.IsSuccess);
            Assert.Equal(5, result.Value.P.X);
        }

        [Fact]
        public void ValueTypeSource_Maps()
        {
            var result = ValueSourceMappings.TryMap(new Point(3, 4));

            Assert.True(result.IsSuccess);
            Assert.Equal(new PointDto(3, 4), result.Value);
        }

        [Fact]
        public void BuiltInValueTypeSource_Maps()
        {
            var result = ValueSourceMappings.TryMap(1.25m);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Value.Scale);
        }
    }
}

namespace ZeroAlloc.Mapping.Tests.TryMapChain
{
    public readonly record struct NonEmpty
    {
        public NonEmpty(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("empty", nameof(text));
            Text = text;
        }

        public string Text { get; }
    }

    public sealed record Inner(string Value);
    public sealed record InnerDto(NonEmpty Value);
    public sealed record Outer(int Id, Inner Child);
    public sealed record OuterDto(int Id, InnerDto Child);
    public sealed record Optional(Inner? Child);
    public sealed record OptionalDto(InnerDto? Child);
    public sealed record Batch(List<Inner> Items);
    public sealed record BatchDto(InnerDto[] Items);
    public sealed record Plain(int X);
    public sealed record PlainDto(int X);
    public sealed record Mixed(Plain P);
    public sealed record MixedDto(PlainDto P);

    [TryMap<Inner, InnerDto>]
    [TryMap<Outer, OuterDto>]
    [TryMap<Optional, OptionalDto>]
    [TryMap<Batch, BatchDto>]
    [Map<Plain, PlainDto>]
    [TryMap<Mixed, MixedDto>]
    public static partial class ChainMappings { }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    public readonly record struct Point(int X, int Y);
    public sealed record PointDto(int X, int Y);
    public sealed record DecimalParts(byte Scale);

    [TryMap<Point, PointDto>]
    [TryMap<decimal, DecimalParts>]
    public static partial class ValueSourceMappings { }
}
