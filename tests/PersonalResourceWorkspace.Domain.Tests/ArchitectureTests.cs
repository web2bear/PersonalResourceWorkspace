using PersonalResourceWorkspace.Domain.Architecture;

namespace PersonalResourceWorkspace.Domain.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainAssemblyHasNoProjectLayerDependencies()
    {
        var references = typeof(DomainAssembly).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name?.StartsWith("PersonalResourceWorkspace.", StringComparison.Ordinal) is true);
    }
}
