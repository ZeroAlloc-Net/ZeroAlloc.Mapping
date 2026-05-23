namespace ZeroAlloc.Mapping.Tests.TestFixtures;

public sealed record ProjSrc(int Id, string Name);
public sealed record ProjDst(int Id, string Name);

[ZeroAlloc.Mapping.Map<ProjSrc, ProjDst>(Projection = true)]
public static partial class ProjMappings { }

// CycleSafe fixtures — note: classes with parameterless ctor + settable properties
// (cycle-safe emit requires the mutable-builder pattern; records won't work).

public sealed class NodeSrc { public string Name { get; set; } = ""; public NodeSrc? Self { get; set; } }
public sealed class NodeDst { public string Name { get; set; } = ""; public NodeDst? Self { get; set; } }

[ZeroAlloc.Mapping.Map<NodeSrc, NodeDst>(CycleSafe = true)]
public static partial class NodeMappings { }

public sealed class CustomerSrc { public string Name { get; set; } = ""; public OrderSrc? LastOrder { get; set; } }
public sealed class OrderSrc { public int Id { get; set; } public CustomerSrc? Customer { get; set; } }
public sealed class CustomerDst { public string Name { get; set; } = ""; public OrderDst? LastOrder { get; set; } }
public sealed class OrderDst { public int Id { get; set; } public CustomerDst? Customer { get; set; } }

[ZeroAlloc.Mapping.Map<OrderSrc, OrderDst>(CycleSafe = true)]
[ZeroAlloc.Mapping.Map<CustomerSrc, CustomerDst>(CycleSafe = true)]
public static partial class OrderMappings { }

// DeepClone fixtures.
public sealed class Tag
{
    public string Name { get; set; } = "";
    public int Id { get; set; }
}

public sealed class DcSrc
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<Tag>? Tags { get; set; }
}

public sealed class DcDst
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<Tag>? Tags { get; set; }
}

[ZeroAlloc.Mapping.Map<DcSrc, DcDst>(DeepClone = true)]
public static partial class DcMappings { }

public sealed class DcCustomer
{
    public string Name { get; set; } = "";
    public int Age { get; set; }
}

public sealed class DcOrderSrc
{
    public int Id { get; set; }
    public DcCustomer? Customer { get; set; }
}

public sealed class DcOrderDst
{
    public int Id { get; set; }
    public DcCustomer? Customer { get; set; }
}

[ZeroAlloc.Mapping.Map<DcOrderSrc, DcOrderDst>(DeepClone = true)]
public static partial class DcOrderMappings { }
