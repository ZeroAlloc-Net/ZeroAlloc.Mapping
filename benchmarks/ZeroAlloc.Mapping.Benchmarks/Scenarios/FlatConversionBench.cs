using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class FlatConversionBench
{
    private ConvSrc _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new ConvSrc(42, "Active", 100, "2026-05-09T12:00:00Z", "200");
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public ConvDst HandWritten_() => HandWritten.MapConv(_src);
    [Benchmark] public ConvDst ZeroAlloc_() => ZaConv.Map(_src);
    [Benchmark] public ConvDst Mapperly_() => MapperlyConv.Map(_src);
    [Benchmark] public ConvDst AutoMapper_() => _autoMapper.Map<ConvDst>(_src);
}
