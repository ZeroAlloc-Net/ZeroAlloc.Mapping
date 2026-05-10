using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

public static class HandWritten
{
    public static FlatDst MapFlat(FlatSrc s) => new(
        s.Id, s.Name, s.Email, s.Age, s.Active, s.Score, s.Version, s.Country);

    public static ConvDst MapConv(ConvSrc s) => new(
        new OrderId(s.Id),
        Enum.Parse<Status>(s.Status),
        s.Count,
        DateTime.Parse(s.Created, System.Globalization.CultureInfo.InvariantCulture),
        int.Parse(s.Quantity, System.Globalization.CultureInfo.InvariantCulture));

    public static OrderFlat MapFlatten(OrderSrc s) => new(
        s.OrderId, s.Customer.Id, s.Customer.Name,
        s.Customer.Address.Street, s.Customer.Address.City, s.Customer.Address.Zip,
        s.Total);

    public static List<FlatDst> MapList(List<FlatSrc> src)
    {
        var dst = new List<FlatDst>(src.Count);
        for (var i = 0; i < src.Count; i++) dst.Add(MapFlat(src[i]));
        return dst;
    }

    public static AnimalDto MapAnimal(Animal a) => a switch
    {
        Dog d => new DogDto(d.Name, d.Breed),
        Cat c => new CatDto(c.Name, c.Indoor),
        Bird b => new BirdDto(b.Name, b.Wingspan),
        _ => throw new NotSupportedException()
    };

    public static void UpdateInPlace(FlatSrc s, FlatDstMutable d)
    {
        d.Id = s.Id; d.Name = s.Name; d.Email = s.Email; d.Age = s.Age;
        d.Active = s.Active; d.Score = s.Score; d.Version = s.Version; d.Country = s.Country;
    }
}
