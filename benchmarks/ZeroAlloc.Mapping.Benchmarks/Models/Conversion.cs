namespace ZeroAlloc.Mapping.Benchmarks.Models;

public enum Status { Active, Inactive, Pending }

public readonly record struct OrderId(int Value);

public sealed record ConvSrc(int Id, string Status, int Count, string Created);
public sealed record ConvDst(OrderId Id, Status Status, long Count, DateTime Created);
