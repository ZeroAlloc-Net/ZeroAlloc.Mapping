using BenchmarkDotNet.Attributes;
using ZeroAlloc.Mapping;
using ZeroAlloc.Mapping.Benchmarks.Mappers;
using ZeroAlloc.Mapping.Benchmarks.Models;
using ZeroAlloc.Results;

namespace ZeroAlloc.Mapping.Benchmarks.Scenarios;

[MemoryDiagnoser]
public class TryMapBench
{
    private ConvSrc _src = null!;

    [GlobalSetup]
    public void Setup()
    {
        _src = new ConvSrc(42, "Active", 100, "2026-05-09T12:00:00Z", "200");
    }

    [Benchmark(Baseline = true)] public ConvDst HandWritten_() => HandWritten.MapConv(_src);
    [Benchmark] public Result<ConvDst, MappingError> ZeroAlloc_() => ZaTry.TryMap(_src);

    // Mapperly has no native Result-type — wrap in try/catch for fairness:
    [Benchmark]
    public ConvDst Mapperly_()
    {
        try { return MapperlyConv.Map(_src); }
        catch { throw; }
    }
}
