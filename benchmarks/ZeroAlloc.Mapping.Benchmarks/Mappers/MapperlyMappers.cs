using Riok.Mapperly.Abstractions;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

[Mapper]
public static partial class MapperlyFlat
{
    public static partial FlatDst Map(FlatSrc src);
}

[Mapper]
public static partial class MapperlyConv
{
    // ConvSrc.Id (int) -> ConvDst.Id (OrderId): Mapperly resolves the
    // single-arg ctor automatically. ConvSrc.Status (string) -> Status enum
    // and ConvSrc.Created (string) -> DateTime are handled by Mapperly's
    // built-in conversions.
    public static partial ConvDst Map(ConvSrc src);
}

[Mapper]
public static partial class MapperlyFlatten
{
    [MapProperty("Customer.Id", nameof(OrderFlat.CustomerId))]
    [MapProperty("Customer.Name", nameof(OrderFlat.CustomerName))]
    [MapNestedProperties("Customer.Address")]
    public static partial OrderFlat Map(OrderSrc src);
}

[Mapper]
public static partial class MapperlyPoly
{
    [MapDerivedType<Dog, DogDto>]
    [MapDerivedType<Cat, CatDto>]
    [MapDerivedType<Bird, BirdDto>]
    public static partial AnimalDto Map(Animal src);
}

[Mapper]
public static partial class MapperlyUpdate
{
    public static partial void Update(FlatSrc src, FlatDstMutable dst);
}
