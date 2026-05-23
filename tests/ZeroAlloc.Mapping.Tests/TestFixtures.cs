namespace ZeroAlloc.Mapping.Tests.TestFixtures;

public sealed record ProjSrc(int Id, string Name);
public sealed record ProjDst(int Id, string Name);

[ZeroAlloc.Mapping.Map<ProjSrc, ProjDst>(Projection = true)]
public static partial class ProjMappings { }
