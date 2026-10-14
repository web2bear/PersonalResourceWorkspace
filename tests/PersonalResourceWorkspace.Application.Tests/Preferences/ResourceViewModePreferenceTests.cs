using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Application.Tests.Preferences;

public sealed class ResourceViewModePreferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unexpected")]
    [InlineData("grid")]
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
    [InlineData(ResourceViewMode.Grid, "grid")]
    [InlineData(ResourceViewMode.List, "list")]
    public void SerializeUsesStableLocalValues(ResourceViewMode mode, string expected)
    {
        Assert.Equal(expected, ResourceViewModePreference.Serialize(mode));
    }
}
