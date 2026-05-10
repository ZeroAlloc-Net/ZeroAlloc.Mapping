using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class UpdateInPlaceBench
{
    private FlatSrc _src = null!;
    private FlatDstMutable _dst = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new FlatSrc(42, "Marcel", "m@example.com", 30, true, 99.5, 7, "NL");
        _dst = new FlatDstMutable();
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public void HandWritten_() => HandWritten.UpdateInPlace(_src, _dst);
    [Benchmark] public void ZeroAlloc_() => ZaUpdate.Map(_src, _dst);
    [Benchmark] public void Mapperly_() => MapperlyUpdate.Update(_src, _dst);
    [Benchmark] public void AutoMapper_() => _autoMapper.Map(_src, _dst);
}
