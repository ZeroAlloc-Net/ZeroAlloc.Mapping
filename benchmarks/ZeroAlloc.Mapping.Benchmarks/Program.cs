using BenchmarkDotNet.Running;
using ZeroAlloc.Mapping.Benchmarks;

Sanity.AssertParity();

var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
return summaries.Any(s => s.HasCriticalValidationErrors) ? 1 : 0;
