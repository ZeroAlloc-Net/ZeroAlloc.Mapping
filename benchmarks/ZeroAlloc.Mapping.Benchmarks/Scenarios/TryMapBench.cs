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

    // Mapperly has no native fallible-mapping primitive; this row measures its
    // non-fallible Map call as a reference point for the cost ZA's TryMap pays
    // beyond happy-path mapping. AutoMapper is omitted entirely since it has
    // no equivalent. See docs/performance.md for the full caveat.
    [Benchmark] public ConvDst Mapperly_() => MapperlyConv.Map(_src);
}
