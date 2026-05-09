using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatteningBench
{
    private OrderSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new OrderSrc(
            OrderId: 7,
            Customer: new Customer(42, "Marcel", new Address("Main 1", "Amsterdam", "1011AA")),
            Total: 99.99m);
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public OrderFlat HandWritten_() => HandWritten.MapFlatten(_src);
    [Benchmark] public OrderFlat ZeroAlloc_() => ZaFlatten.Map(_src);
    [Benchmark] public OrderFlat Mapperly_() => MapperlyFlatten.Map(_src);
    [Benchmark] public OrderFlat AutoMapper_() => _autoMapper.Map<OrderFlat>(_src);
}
