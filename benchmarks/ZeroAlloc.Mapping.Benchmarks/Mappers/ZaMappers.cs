using ZeroAlloc.Mapping;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

[Map<FlatSrc, FlatDst>]
public static partial class ZaFlat { }

[Map<ConvSrc, ConvDst>]
public static partial class ZaConv { }

[Map<OrderSrc, OrderFlat>]
public static partial class ZaFlatten
{
    [MapProperty("Customer.Id", "CustomerId")]
    [MapProperty("Customer.Name", "CustomerName")]
    [MapProperty("Customer.Address.Street", "Street")]
    [MapProperty("Customer.Address.City", "City")]
    [MapProperty("Customer.Address.Zip", "Zip")]
    public static partial OrderFlat Map(OrderSrc src);
}

[Map<Dog, DogDto>]
[Map<Cat, CatDto>]
[Map<Bird, BirdDto>]
[PolymorphicMap<Animal, AnimalDto>]
public static partial class ZaPoly { }

[Map<FlatSrc, FlatDstMutable>]
public static partial class ZaUpdate
{
    public static partial void Map(FlatSrc src, FlatDstMutable existingDst);
}

[TryMap<ConvSrc, ConvDst>]
public static partial class ZaTry { }
