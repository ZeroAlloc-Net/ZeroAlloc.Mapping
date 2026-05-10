namespace ZeroAlloc.Mapping.Benchmarks.Models;

public sealed record Address(string Street, string City, string Zip);
public sealed record Customer(int Id, string Name, Address Address);
public sealed record OrderSrc(int OrderId, Customer Customer, decimal Total);

public sealed record OrderFlat(int OrderId, int CustomerId, string CustomerName,
    string Street, string City, string Zip, decimal Total);
