using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Application.Tests.Preferences;

public sealed class ResourceViewModePreferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unexpected")]
    [InlineData("grid")]
    [InlineData("grid-medium")]
    public void ParseUsesGridAsSafeDefault(string? value)
    {
        Assert.Equal(ResourceViewMode.Grid, ResourceViewModePreference.Parse(value));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("LIST")]
    public void ParseRestoresListValue(string value)
    {
        Assert.Equal(ResourceViewMode.List, ResourceViewModePreference.Parse(value));
    }

    [Theory]
    [InlineData(ResourceViewMode.Grid, "grid-medium")]
    [InlineData(ResourceViewMode.GridSmall, "grid-small")]
    [InlineData(ResourceViewMode.GridLarge, "grid-large")]
    [InlineData(ResourceViewMode.List, "list")]
    public void SerializeUsesStableLocalValues(ResourceViewMode mode, string expected)
    {
        Assert.Equal(expected, ResourceViewModePreference.Serialize(mode));
    }

    [Theory]
    [InlineData("grid-small", ResourceViewMode.GridSmall)]
    [InlineData("GRID-LARGE", ResourceViewMode.GridLarge)]
    [InlineData("grid-medium", ResourceViewMode.Grid)]
    public void ParseRestoresGridSizes(string value, ResourceViewMode expected)
    {
        Assert.Equal(expected, ResourceViewModePreference.Parse(value));
    }
}
