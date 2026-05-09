using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class PolymorphicBench
{
    private Animal _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new Dog("Rex", "Labrador");
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public AnimalDto HandWritten_() => HandWritten.MapAnimal(_src);
    [Benchmark] public AnimalDto ZeroAlloc_() => ZaPoly.Map(_src);
    [Benchmark] public AnimalDto Mapperly_() => MapperlyPoly.Map(_src);
    [Benchmark] public AnimalDto AutoMapper_() => _autoMapper.Map<AnimalDto>(_src);
}
