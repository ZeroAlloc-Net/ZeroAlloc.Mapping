using Microsoft.CodeAnalysis;

namespace ZeroAlloc.Mapping.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class MappingGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterSourceOutput(context.CompilationProvider, (spc, comp) =>
        {
            var classes = MapperDiscovery.Discover(comp).ToList();
            if (classes.Count == 0) return;

            foreach (var c in classes)
            {
                var src = MapEmitter.Emit(c, comp);
                spc.AddSource($"{c.ClassName}.g.cs", src);
            }
        });
    }
}
