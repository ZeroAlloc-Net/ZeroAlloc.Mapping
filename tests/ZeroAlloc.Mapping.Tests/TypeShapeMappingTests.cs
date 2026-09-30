namespace ZeroAlloc.Mapping.Tests
{
    using TypeShapes;

    /// <summary>
    /// Mappings whose source or destination is a nested type, a constructed generic, a generic
    /// over a nested type, or a type named with a verbatim identifier are generated and map
    /// their values (#136). They used to be skipped with no diagnostic. The verbatim case uses a
    /// namespace named <c>@event</c>; a type named with a keyword is covered by the generator
    /// tests, because a lower-case type name draws CS8981 here.
    /// </summary>
    public class TypeShapeMappingTests
    {
        [Fact]
        public void Map_NestedTypes_RoundTrips()
        {
            var dst = ShapeMappings.Map(new Dtos.Src(7, "seven"));
            Assert.Equal(new Dtos.Dst(7, "seven"), dst);
        }

        [Fact]
        public void Map_ClosedGeneric_RoundTrips()
        {
            var dst = ShapeMappings.Map(new Box<int>(3));
            Assert.Equal(new Plain(3), dst);

            var back = ShapeMappings.Map(new Plain(4));
            Assert.Equal(new Box<int>(4), back);
        }

        [Fact]
        public void Map_GenericOfNested_RoundTrips()
        {
            var dst = ShapeMappings.Map(new Box<Dtos.Src>(new Dtos.Src(1, "one")));
            Assert.Equal(new Box<Dtos.Dst>(new Dtos.Dst(1, "one")), dst);
        }

        [Fact]
        public void Map_VerbatimNames_RoundTrips()
        {
            var dst = ShapeMappings.Map(new TypeShapes.@event.Ticket(9));
            Assert.Equal(new TypeShapes.@event.TicketDto(9), dst);
        }

        [Fact]
        public void TryMap_NestedTypes_RoundTrips()
        {
            var result = ShapeTryMappings.TryMap(new Dtos.Src(7, "seven"));
            Assert.True(result.IsSuccess);
            Assert.Equal(new Dtos.Dst(7, "seven"), result.Value);
        }

        [Fact]
        public void TryMap_ClosedGeneric_RoundTrips()
        {
            var result = ShapeTryMappings.TryMap(new Box<int>(3));
            Assert.True(result.IsSuccess);
            Assert.Equal(new Plain(3), result.Value);
        }

        [Fact]
        public void TryMap_GenericOfNested_RoundTrips()
        {
            var result = ShapeTryMappings.TryMap(new Box<Dtos.Src>(new Dtos.Src(1, "one")));
            Assert.True(result.IsSuccess);
            Assert.Equal(new Carton<Dtos.Src>(new Dtos.Src(1, "one")), result.Value);
        }

        [Fact]
        public void TryMap_VerbatimNames_RoundTrips()
        {
            var result = ShapeTryMappings.TryMap(new TypeShapes.@event.Ticket(9));
            Assert.True(result.IsSuccess);
            Assert.Equal(new TypeShapes.@event.TicketDto(9), result.Value);
        }
    }
}

namespace ZeroAlloc.Mapping.Tests.TypeShapes
{
    public static class Dtos
    {
        public sealed record Src(int Id, string Name);
        public sealed record Dst(int Id, string Name);
    }

    public sealed record Box<T>(T X);
    public sealed record Carton<T>(T X);
    public sealed record Plain(int X);

    [Map<Dtos.Src, Dtos.Dst>]
    [Map<Box<int>, Plain>]
    [Map<Plain, Box<int>>]
    [Map<Box<Dtos.Src>, Box<Dtos.Dst>>]
    [Map<@event.Ticket, @event.TicketDto>]
    public static partial class ShapeMappings { }

    [TryMap<Dtos.Src, Dtos.Dst>]
    [TryMap<Box<int>, Plain>]
    [TryMap<Box<Dtos.Src>, Carton<Dtos.Src>>]
    [TryMap<@event.Ticket, @event.TicketDto>]
    public static partial class ShapeTryMappings { }
}

namespace ZeroAlloc.Mapping.Tests.TypeShapes.@event
{
    public sealed record Ticket(int X);
    public sealed record TicketDto(int X);
}
