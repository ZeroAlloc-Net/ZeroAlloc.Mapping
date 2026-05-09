using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Mappers;

public sealed class BenchmarkProfile : Profile
{
    public BenchmarkProfile()
    {
        CreateMap<FlatSrc, FlatDst>();
        CreateMap<FlatSrc, FlatDstMutable>();

        CreateMap<ConvSrc, ConvDst>()
            .ForMember(d => d.Id, o => o.MapFrom(s => new OrderId(s.Id)))
            .ForMember(d => d.Status, o => o.MapFrom(s => Enum.Parse<Status>(s.Status)))
            .ForMember(d => d.Created, o => o.MapFrom(s =>
                DateTime.Parse(s.Created, System.Globalization.CultureInfo.InvariantCulture)));

        CreateMap<OrderSrc, OrderFlat>()
            .ForMember(d => d.CustomerId, o => o.MapFrom(s => s.Customer.Id))
            .ForMember(d => d.CustomerName, o => o.MapFrom(s => s.Customer.Name))
            .ForMember(d => d.Street, o => o.MapFrom(s => s.Customer.Address.Street))
            .ForMember(d => d.City, o => o.MapFrom(s => s.Customer.Address.City))
            .ForMember(d => d.Zip, o => o.MapFrom(s => s.Customer.Address.Zip));

        CreateMap<Animal, AnimalDto>()
            .Include<Dog, DogDto>()
            .Include<Cat, CatDto>()
            .Include<Bird, BirdDto>();
        CreateMap<Dog, DogDto>();
        CreateMap<Cat, CatDto>();
        CreateMap<Bird, BirdDto>();
    }
}

public static class AutoMapperFactory
{
    public static IMapper Build()
    {
        var config = new MapperConfiguration(
            c => c.AddProfile<BenchmarkProfile>(),
            NullLoggerFactory.Instance);
        return config.CreateMapper();
    }
}
