using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatIdentityBench
{
    private FlatSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new FlatSrc(42, "Marcel", "m@example.com", 30, true, 99.5, 7, "NL");
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public FlatDst HandWritten_() => HandWritten.MapFlat(_src);
    [Benchmark] public FlatDst ZeroAlloc_() => ZaFlat.Map(_src);
    [Benchmark] public FlatDst Mapperly_() => MapperlyFlat.Map(_src);
    [Benchmark] public FlatDst AutoMapper_() => _autoMapper.Map<FlatDst>(_src);
}
