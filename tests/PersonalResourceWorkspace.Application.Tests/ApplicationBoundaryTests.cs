using PersonalResourceWorkspace.Application.Abstractions;

namespace PersonalResourceWorkspace.Application.Tests;

public sealed class ApplicationBoundaryTests
{
    [Fact]
    public void InitializerRequiresCancellationToken()
    {
        var method = typeof(IApplicationInitializer).GetMethod(nameof(IApplicationInitializer.InitializeAsync));

        Assert.NotNull(method);
        Assert.Equal(typeof(CancellationToken), method.GetParameters().Single().ParameterType);
    }
}
