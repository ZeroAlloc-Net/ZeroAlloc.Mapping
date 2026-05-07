namespace ZeroAlloc.Mapping.AotSmoke.Internal;

internal static class AllocationGate
{
    public static void AssertBudget(int budgetBytes, int iterations, Action action, string label)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (iterations < 1) throw new ArgumentOutOfRangeException(nameof(iterations));

        action();
        action();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++) action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var perCall = allocated / iterations;
        var totalBudget = (long)budgetBytes * iterations;
        if (allocated > totalBudget)
        {
            throw new InvalidOperationException(
                $"AllocationGate: {label} allocated {allocated} B total over {iterations} iterations " +
                $"(~{perCall} B/call avg), budget is {budgetBytes} B/call ({totalBudget} B total). " +
                "Use BenchmarkDotNet [MemoryDiagnoser] locally to find the culprit.");
        }
    }
}
