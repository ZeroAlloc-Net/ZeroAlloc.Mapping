using System.Linq;
using Xunit;
using ZeroAlloc.Mapping;

namespace ZeroAlloc.Mapping.Tests;

public sealed class CycleSafeDeepCloneTests
{
    [Fact]
    public void CyclicGraphThroughParameterlessCtorType_ClonesWithoutLoop()
    {
        var a = new Node { Name = "a" };
        var b = new Node { Name = "b" };
        a.Next = b;
        b.Next = a;   // A→B→A cycle.

        var cloned = CycleSafeMappers.Map(a);

        // Identity: clone is a new instance, but Next->Next cycles back to the SAME clone of a
        // (not the original a, not a fresh allocation). Tracker dedup'd it.
        Assert.NotSame(a, cloned);
        Assert.NotSame(b, cloned.Next);
        Assert.Same(cloned, cloned.Next!.Next);
        Assert.Equal("a", cloned.Name);
        Assert.Equal("b", cloned.Next!.Name);
    }

    [Fact]
    public void DiamondAliasing_LiteralWalkedType_SharesClone()
    {
        var shared = new Leaf { Value = "shared" };
        var diamond = new Diamond
        {
            Left = new Branch { Leaf = shared },
            Right = new Branch { Leaf = shared },
        };

        var cloned = CycleSafeMappers.Map(diamond);

        Assert.NotSame(diamond, cloned);
        Assert.NotSame(shared, cloned.Left!.Leaf);
        Assert.Same(cloned.Left!.Leaf, cloned.Right!.Leaf);   // diamond preserved
        Assert.Equal("shared", cloned.Left!.Leaf!.Value);
    }

    [Fact]
    public void CollectionOfCloneOnlyType_WithCycles_HandledCorrectly()
    {
        var x = new Item { Tag = "x" };
        var y = new Item { Tag = "y" };
        x.Buddies = new System.Collections.Generic.List<Item> { y };
        y.Buddies = new System.Collections.Generic.List<Item> { x };  // cycle through collection.

        var cloned = CycleSafeMappers.Map(x);

        Assert.NotSame(x, cloned);
        Assert.NotSame(y, cloned.Buddies![0]);
        Assert.Same(cloned, cloned.Buddies![0].Buddies![0]);
        Assert.Equal("x", cloned.Tag);
        Assert.Equal("y", cloned.Buddies![0].Tag);
    }

    [Fact]
    public void MixedGraph_ExplicitNestedAndLiteralWalked_ShareSameTracker()
    {
        var detail = new Detail { Note = "d" };
        var owner = new Owner { Name = "o", Detail = detail };
        var wrapper = new Wrapper { Owner = owner };
        wrapper.SelfRef = wrapper;   // cycle in wrapper.

        var cloned = CycleSafeMappers.Map(wrapper);

        Assert.Same(cloned, cloned.SelfRef);   // tracker resolved the cycle through literal-walked Wrapper.
        Assert.NotSame(detail, cloned.Owner!.Detail);
        Assert.NotSame(owner, cloned.Owner);
        Assert.Equal("d", cloned.Owner!.Detail!.Note);
    }
}

public sealed class Node { public string Name { get; set; } = ""; public Node? Next { get; set; } }
public sealed class Leaf { public string Value { get; set; } = ""; }
public sealed class Branch { public Leaf? Leaf { get; set; } }
public sealed class Diamond { public Branch? Left { get; set; } public Branch? Right { get; set; } }
public sealed class Item { public string Tag { get; set; } = ""; public System.Collections.Generic.List<Item>? Buddies { get; set; } }

public sealed class Detail { public string Note { get; set; } = ""; }
public sealed class Owner { public string Name { get; set; } = ""; public Detail? Detail { get; set; } }
public sealed class Wrapper { public Owner? Owner { get; set; } public Wrapper? SelfRef { get; set; } }

[Map<Node, Node>(DeepClone = true, CycleSafe = true)]
[Map<Diamond, Diamond>(DeepClone = true, CycleSafe = true)]
[Map<Item, Item>(DeepClone = true, CycleSafe = true)]
[Map<Owner, Owner>(DeepClone = true, CycleSafe = true)]
[Map<Wrapper, Wrapper>(DeepClone = true, CycleSafe = true)]
public static partial class CycleSafeMappers { }
