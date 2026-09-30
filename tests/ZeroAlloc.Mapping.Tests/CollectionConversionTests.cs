namespace ZeroAlloc.Mapping.Tests
{
    using CollectionConversion;

    /// <summary>
    /// A collection member whose elements convert implicitly maps element by element (#143).
    /// </summary>
    public class CollectionConversionTests
    {
        [Fact]
        public void Map_ConvertsEveryElement()
        {
            var dst = WidenMappings.Map(new Narrow(new List<int> { 1, 2 }, new[] { 3 }, null));

            Assert.Equal(new long[] { 1, 2 }, dst.Values);
            Assert.Equal(new long[] { 3 }, dst.Array);
            Assert.Null(dst.Optional);
        }

        [Fact]
        public void TryMap_ConvertsEveryElement()
        {
            var result = WidenMappings.TryMap(new Narrow(new List<int> { 1, 2 }, new[] { 3 }, new List<int> { 4 }));

            Assert.True(result.IsSuccess);
            Assert.Equal(new long[] { 1, 2 }, result.Value.Values);
            Assert.Equal(new long[] { 3 }, result.Value.Array);
            Assert.Equal(new long[] { 4 }, result.Value.Optional!);
        }
    }
}

namespace ZeroAlloc.Mapping.Tests.CollectionConversion
{
    public sealed record Narrow(List<int> Values, int[] Array, List<int>? Optional);
    public sealed record Wide(List<long> Values, long[] Array, IReadOnlyList<long>? Optional);

    [Map<Narrow, Wide>]
    [TryMap<Narrow, Wide>]
    public static partial class WidenMappings { }
}
