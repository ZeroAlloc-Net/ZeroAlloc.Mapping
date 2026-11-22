using ZeroAlloc.Mapping;
using ZeroAlloc.Mapping.Sdk10Smoke;

// Mappings.Map exists only if the compiler loaded the generator.
var order = Mappings.Map(new OrderRequest(7, "hello"));
if (order.Id != 7 || order.Notes != "hello")
{
    Console.Error.WriteLine($"FAIL: mapped {order.Id} and '{order.Notes}'");
    return 1;
}

Console.WriteLine("SDK 10 consumer: generator loaded, request mapped by generated code.");
return 0;

namespace ZeroAlloc.Mapping.Sdk10Smoke
{
    public sealed record OrderRequest(int Id, string Notes);

    public sealed record Order(int Id, string Notes);

    [Map<OrderRequest, Order>]
    public static partial class Mappings;
}
