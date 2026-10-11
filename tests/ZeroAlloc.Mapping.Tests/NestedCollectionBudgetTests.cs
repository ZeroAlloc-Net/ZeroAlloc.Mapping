using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Mapping.Tests
{
    using NestedCollections;

    /// <summary>
    /// A nested collection member allocates exactly its destination: the destination object, the
    /// destination collection and its elements. Each budget is the measured cost of building that
    /// same destination by hand, so any extra allocation, such as a LINQ iterator, a closure or a
    /// re-grown list, fails the test (#153).
    /// </summary>
    public class NestedCollectionBudgetTests
    {
        private const int Count = 16;
        private const int Iterations = 500;

        private static readonly List<NcItem> Items = BuildItems();

        // ---- [Map]: one test per destination collection shape ----

        [Fact]
        public void Map_ListMember_AllocatesOnlyTheDestination()
        {
            var src = new ListSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new ListDst(ItemList()),
                "[Map] List<T> member");
        }

        [Fact]
        public void Map_ArrayMember_AllocatesOnlyTheDestination()
        {
            var src = new ArraySrc(Items.ToArray());
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new ArrayDst(ItemArray()),
                "[Map] T[] member");
        }

        [Fact]
        public void Map_ReadOnlyListMember_AllocatesOnlyTheDestination()
        {
            var src = new ReadOnlyListSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new ReadOnlyListDst(ItemList()),
                "[Map] IReadOnlyList<T> member");
        }

        [Fact]
        public void Map_IListMember_AllocatesOnlyTheDestination()
        {
            var src = new IListSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new IListDst(ItemList()),
                "[Map] IList<T> member");
        }

        [Fact]
        public void Map_CollectionMember_AllocatesOnlyTheDestination()
        {
            var src = new CollectionSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new CollectionDst(ItemList()),
                "[Map] ICollection<T> member");
        }

        [Fact]
        public void Map_ReadOnlyCollectionMember_AllocatesOnlyTheDestination()
        {
            var src = new ReadOnlyCollectionSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new ReadOnlyCollectionDst(ItemList()),
                "[Map] IReadOnlyCollection<T> member");
        }

        [Fact]
        public void Map_EnumerableMember_AllocatesOnlyTheDestination()
        {
            var src = new EnumerableSrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new EnumerableDst(ItemList()),
                "[Map] IEnumerable<T> member");
        }

        [Fact]
        public void Map_ListToArrayMember_AllocatesOnlyTheDestination()
        {
            var src = new ListToArraySrc(Items);
            AssertDestinationOnly(
                () => NestedCollectionMappings.Map(src),
                () => new ArrayDst(ItemArray()),
                "[Map] List<T> member into T[]");
        }

        [Fact]
        public void Map_UpdateInPlace_ListMember_AllocatesOnlyTheDestinationList()
        {
            var src = new MutableListSrc { Items = Items };
            var dst = new MutableListDst();
            AssertDestinationOnly(
                () => { NestedCollectionUpdateMappings.Map(src, dst); return dst; },
                ItemList,
                "[Map] update-in-place List<T> member");
        }

        // ---- implicit element conversion, through ConversionResolver ----

        [Fact]
        public void Map_ConvertedListMember_AllocatesOnlyTheDestination()
        {
            var src = new NarrowList(Numbers());
            AssertDestinationOnly(
                () => NestedConversionMappings.Map(src),
                () => new WideList(new List<long>(Count)),
                "[Map] List<int> member into List<long>");
        }

        [Fact]
        public void Map_ConvertedArrayMember_AllocatesOnlyTheDestination()
        {
            var src = new NarrowArray(Numbers().ToArray());
            AssertDestinationOnly(
                () => NestedConversionMappings.Map(src),
                () => new WideArray(new long[Count]),
                "[Map] int[] member into long[]");
        }

        [Fact]
        public void TryMap_ConvertedListMember_AllocatesOnlyTheDestinationAndResult()
        {
            var src = new NarrowList(Numbers());
            var expected = DestinationBytes(() => new WideList(new List<long>(Count)));
            // The Result<WideList, MappingError> is a struct, so nothing is added for it.
            AllocationGate.AssertBudget(expected, Iterations, () => _ = NestedConversionMappings.TryMap(src),
                "[TryMap] List<int> member into List<long>");
        }

        // ---- [Map(CycleSafe = true)]: the documented tracker plus the destination ----

        [Fact]
        public void Map_CycleSafe_ListMember_AllocatesOnlyTheTrackerAndDestination()
        {
            var src = new CsParent { Children = Children() };
            AssertDestinationOnly(
                () => CycleSafeCollectionMappings.Map(src),
                () =>
                {
                    var tracker = Tracker();
                    var dst = new CsParentDto();
                    tracker.Add(src, dst);
                    var list = new List<CsChildDto>(Count);
                    for (var i = 0; i < Count; i++) { var d = new CsChildDto(); tracker.Add(src.Children![i], d); list.Add(d); }
                    dst.Children = list;
                    return dst;
                },
                "[Map(CycleSafe)] List<T> member");
        }

        [Fact]
        public void Map_CycleSafe_ArrayMember_AllocatesOnlyTheTrackerAndDestination()
        {
            var src = new CsArrayParent { Children = Children().ToArray() };
            AssertDestinationOnly(
                () => CycleSafeCollectionMappings.Map(src),
                () =>
                {
                    var tracker = Tracker();
                    var dst = new CsArrayParentDto();
                    tracker.Add(src, dst);
                    var array = new CsChildDto[Count];
                    for (var i = 0; i < Count; i++) { var d = new CsChildDto(); tracker.Add(src.Children![i], d); array[i] = d; }
                    dst.Children = array;
                    return dst;
                },
                "[Map(CycleSafe)] T[] member");
        }

        // ---- [Map(DeepClone = true)] ----

        [Fact]
        public void DeepClone_ListAndArrayMembers_AllocateOnlyTheClone()
        {
            var src = new DcBag { Tags = Tags(), TagArray = Tags().ToArray() };
            AssertDestinationOnly(
                () => DeepCloneCollectionMappings.Map(src),
                () => new DcBag { Tags = CloneTags(new List<DcTag>(Count)), TagArray = CloneTagArray() },
                "[Map(DeepClone)] List<T> and T[] members");
        }

        [Fact]
        public void DeepClone_CycleSafe_NestedCollections_AllocateOnlyTheTrackerAndClone()
        {
            var src = new DcsRoot { Groups = Groups() };
            AssertDestinationOnly(
                () => DeepCloneCycleSafeCollectionMappings.Map(src),
                () =>
                {
                    var tracker = Tracker();
                    var dst = new DcsRoot();
                    tracker.Add(src, dst);
                    var groups = new List<DcsGroup>(src.Groups!.Count);
                    for (var gi = 0; gi < src.Groups.Count; gi++)
                    {
                        var g = src.Groups[gi];
                        var gd = new DcsGroup();
                        tracker.Add(g, gd);
                        var leaves = new DcsLeaf[g.Leaves!.Length];
                        for (var i = 0; i < leaves.Length; i++)
                        {
                            var ld = new DcsLeaf();
                            tracker.Add(g.Leaves[i], ld);
                            ld.Id = g.Leaves[i].Id;
                            leaves[i] = ld;
                        }
                        gd.Leaves = leaves;
                        groups.Add(gd);
                    }
                    dst.Groups = groups;
                    return dst;
                },
                "[Map(DeepClone, CycleSafe)] List<T> of T[] members");
        }

        // ---- the mapped values and their order are kept ----

        [Fact]
        public void Map_KeepsElementOrderForEveryShape()
        {
            var expected = Items.Select(i => i.Id).ToArray();

            Assert.Equal(expected, NestedCollectionMappings.Map(new ListSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new ArraySrc(Items.ToArray())).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new ReadOnlyListSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new IListSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new CollectionSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new ReadOnlyCollectionSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new EnumerableSrc(Items)).Items.Select(i => i.Id));
            Assert.Equal(expected, NestedCollectionMappings.Map(new ListToArraySrc(Items)).Items.Select(i => i.Id));
        }

        [Fact]
        public void Map_EnumerableMemberThatIsNotAList_IsEnumeratedInOrder()
        {
            var dst = NestedCollectionMappings.Map(new EnumerableSrc(Yield()));

            Assert.Equal(new[] { 3, 1, 2 }, dst.Items.Select(i => i.Id));

            static IEnumerable<NcItem> Yield()
            {
                yield return new NcItem(3);
                yield return new NcItem(1);
                yield return new NcItem(2);
            }
        }

        [Fact]
        public void Map_NullCollectionMember_StillThrows()
        {
            Assert.Throws<ArgumentNullException>(() => NestedCollectionMappings.Map(new ListSrc(null!)));
        }

        [Fact]
        public void Map_CycleSafe_NullCollectionAndNullElements_StayNull()
        {
            Assert.Null(CycleSafeCollectionMappings.Map(new CsParent { Children = null }).Children);

            var dst = CycleSafeCollectionMappings.Map(new CsParent { Children = [new CsChild { Id = 1 }, null!, new CsChild { Id = 2 }] });

            Assert.Equal(3, dst.Children!.Count);
            Assert.Equal(1, dst.Children[0].Id);
            Assert.Null(dst.Children[1]);
            Assert.Equal(2, dst.Children[2].Id);
        }

        [Fact]
        public void Map_CycleSafe_SharedElement_MapsToOneInstance()
        {
            var shared = new CsChild { Id = 7 };
            var dst = CycleSafeCollectionMappings.Map(new CsParent { Children = [shared, shared] });

            Assert.Same(dst.Children![0], dst.Children[1]);
        }

        [Fact]
        public void DeepClone_NullCollectionMembers_StayNull()
        {
            var dst = DeepCloneCollectionMappings.Map(new DcBag());

            Assert.Null(dst.Tags);
            Assert.Null(dst.TagArray);
        }

        [Fact]
        public void DeepClone_CycleSafe_ClonesNestedCollectionsInOrder()
        {
            var src = new DcsRoot { Groups = Groups() };
            var dst = DeepCloneCycleSafeCollectionMappings.Map(src);

            Assert.Equal(src.Groups!.Count, dst.Groups!.Count);
            for (var g = 0; g < src.Groups.Count; g++)
            {
                Assert.NotSame(src.Groups[g], dst.Groups[g]);
                var srcLeaves = src.Groups[g].Leaves!;
                var dstLeaves = dst.Groups[g].Leaves!;
                Assert.Equal(srcLeaves.Length, dstLeaves.Length);
                for (var i = 0; i < srcLeaves.Length; i++) Assert.Equal(srcLeaves[i].Id, dstLeaves[i].Id);
            }
        }

        // ---- helpers ----

        private static void AssertDestinationOnly(Func<object> map, Func<object> buildDestination, string label) =>
            AllocationGate.AssertBudget(DestinationBytes(buildDestination), Iterations, () => _ = map(), label);

        /// <summary>The bytes one call of <paramref name="build"/> allocates, measured like the gate.</summary>
        private static int DestinationBytes(Func<object> build)
        {
            _ = build();
            _ = build();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < Iterations; i++) _ = build();
            return (int)((GC.GetAllocatedBytesForCurrentThread() - before) / Iterations);
        }

        private static Dictionary<object, object> Tracker() => new(ReferenceEqualityComparer.Instance);

        private static List<NcItem> BuildItems()
        {
            var items = new List<NcItem>(Count);
            for (var i = 0; i < Count; i++) items.Add(new NcItem(i));
            return items;
        }

        private static List<NcItemDto> ItemList()
        {
            var list = new List<NcItemDto>(Count);
            for (var i = 0; i < Count; i++) list.Add(new NcItemDto(i));
            return list;
        }

        private static NcItemDto[] ItemArray()
        {
            var array = new NcItemDto[Count];
            for (var i = 0; i < Count; i++) array[i] = new NcItemDto(i);
            return array;
        }

        private static List<int> Numbers() => Enumerable.Range(0, Count).ToList();

        private static List<CsChild> Children()
        {
            var children = new List<CsChild>(Count);
            for (var i = 0; i < Count; i++) children.Add(new CsChild { Id = i });
            return children;
        }

        private static List<DcTag> Tags()
        {
            var tags = new List<DcTag>(Count);
            for (var i = 0; i < Count; i++) tags.Add(new DcTag { Id = i, Name = "t" });
            return tags;
        }

        private static List<DcTag> CloneTags(List<DcTag> into)
        {
            for (var i = 0; i < Count; i++) into.Add(new DcTag { Id = i, Name = "t" });
            return into;
        }

        private static DcTag[] CloneTagArray()
        {
            var array = new DcTag[Count];
            for (var i = 0; i < Count; i++) array[i] = new DcTag { Id = i, Name = "t" };
            return array;
        }

        private static List<DcsGroup> Groups()
        {
            var groups = new List<DcsGroup>(4);
            for (var g = 0; g < 4; g++)
            {
                var leaves = new DcsLeaf[4];
                for (var i = 0; i < leaves.Length; i++) leaves[i] = new DcsLeaf { Id = (g * 10) + i };
                groups.Add(new DcsGroup { Leaves = leaves });
            }
            return groups;
        }
    }
}

namespace ZeroAlloc.Mapping.Tests.NestedCollections
{
    public sealed record NcItem(int Id);
    public sealed record NcItemDto(int Id);

    public sealed record ListSrc(List<NcItem> Items);
    public sealed record ListDst(List<NcItemDto> Items);
    public sealed record ArraySrc(NcItem[] Items);
    public sealed record ArrayDst(NcItemDto[] Items);
    public sealed record ReadOnlyListSrc(IReadOnlyList<NcItem> Items);
    public sealed record ReadOnlyListDst(IReadOnlyList<NcItemDto> Items);
    public sealed record IListSrc(IList<NcItem> Items);
    public sealed record IListDst(IList<NcItemDto> Items);
    public sealed record CollectionSrc(ICollection<NcItem> Items);
    public sealed record CollectionDst(ICollection<NcItemDto> Items);
    public sealed record ReadOnlyCollectionSrc(IReadOnlyCollection<NcItem> Items);
    public sealed record ReadOnlyCollectionDst(IReadOnlyCollection<NcItemDto> Items);
    public sealed record EnumerableSrc(IEnumerable<NcItem> Items);
    public sealed record EnumerableDst(IEnumerable<NcItemDto> Items);
    public sealed record ListToArraySrc(List<NcItem> Items);

    [Map<NcItem, NcItemDto>]
    [Map<ListSrc, ListDst>]
    [Map<ArraySrc, ArrayDst>]
    [Map<ReadOnlyListSrc, ReadOnlyListDst>]
    [Map<IListSrc, IListDst>]
    [Map<CollectionSrc, CollectionDst>]
    [Map<ReadOnlyCollectionSrc, ReadOnlyCollectionDst>]
    [Map<EnumerableSrc, EnumerableDst>]
    [Map<ListToArraySrc, ArrayDst>]
    public static partial class NestedCollectionMappings { }

    public sealed class MutableListSrc { public List<NcItem> Items { get; set; } = []; }
    public sealed class MutableListDst { public List<NcItemDto> Items { get; set; } = []; }

    [Map<NcItem, NcItemDto>]
    [Map<MutableListSrc, MutableListDst>]
    public static partial class NestedCollectionUpdateMappings
    {
        public static partial void Map(MutableListSrc src, MutableListDst existingDst);
    }

    public sealed record NarrowList(List<int> Values);
    public sealed record WideList(List<long> Values);
    public sealed record NarrowArray(int[] Values);
    public sealed record WideArray(long[] Values);

    [Map<NarrowList, WideList>]
    [TryMap<NarrowList, WideList>]
    [Map<NarrowArray, WideArray>]
    public static partial class NestedConversionMappings { }

    public sealed class CsChild { public int Id { get; set; } }
    public sealed class CsChildDto { public int Id { get; set; } }
    public sealed class CsParent { public List<CsChild>? Children { get; set; } }
    public sealed class CsParentDto { public List<CsChildDto>? Children { get; set; } }
    public sealed class CsArrayParent { public CsChild[]? Children { get; set; } }
    public sealed class CsArrayParentDto { public CsChildDto[]? Children { get; set; } }

    [Map<CsChild, CsChildDto>(CycleSafe = true)]
    [Map<CsParent, CsParentDto>(CycleSafe = true)]
    [Map<CsArrayParent, CsArrayParentDto>(CycleSafe = true)]
    public static partial class CycleSafeCollectionMappings { }

    public sealed class DcTag
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
    }

    public sealed class DcBag
    {
        public List<DcTag>? Tags { get; set; }
        public DcTag[]? TagArray { get; set; }
    }

    [Map<DcBag, DcBag>(DeepClone = true)]
    public static partial class DeepCloneCollectionMappings { }

    public sealed class DcsLeaf { public int Id { get; set; } }
    public sealed class DcsGroup { public DcsLeaf[]? Leaves { get; set; } }
    public sealed class DcsRoot { public List<DcsGroup>? Groups { get; set; } }

    [Map<DcsRoot, DcsRoot>(DeepClone = true, CycleSafe = true)]
    public static partial class DeepCloneCycleSafeCollectionMappings { }
}
