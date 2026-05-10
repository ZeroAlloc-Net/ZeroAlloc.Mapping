namespace ZeroAlloc.Mapping.Benchmarks.Models;

public sealed record FlatSrc(
    int Id, string Name, string Email, int Age,
    bool Active, double Score, long Version, string Country);

public sealed record FlatDst(
    int Id, string Name, string Email, int Age,
    bool Active, double Score, long Version, string Country);

public sealed class FlatDstMutable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public bool Active { get; set; }
    public double Score { get; set; }
    public long Version { get; set; }
    public string Country { get; set; } = "";
}
