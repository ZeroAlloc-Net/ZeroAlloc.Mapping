using AutoMapper;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class CollectionBench
{
    private List<FlatSrc> _src = null!;
    private IMapper _autoMapper = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = Enumerable.Range(0, 1000)
            .Select(i => new FlatSrc(i, "n", "e", i, true, i * 1.5, i, "NL"))
            .ToList();
        _autoMapper = AutoMapperFactory.Build();
    }

    [Benchmark(Baseline = true)] public List<FlatDst> HandWritten_() => HandWritten.MapList(_src);
    [Benchmark] public List<FlatDst> ZeroAlloc_() => ZaFlat.Map(_src);
    [Benchmark] public List<FlatDst> Mapperly_() => _src.Select(MapperlyFlat.Map).ToList();
    [Benchmark] public List<FlatDst> AutoMapper_() => _autoMapper.Map<List<FlatDst>>(_src);
}
