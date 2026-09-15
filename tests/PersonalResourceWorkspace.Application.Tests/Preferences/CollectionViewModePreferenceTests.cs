using PersonalResourceWorkspace.Application.Preferences;

namespace PersonalResourceWorkspace.Application.Tests.Preferences;

public sealed class CollectionViewModePreferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unexpected")]
    [InlineData("grid")]
    public void ParseUsesGridAsSafeDefault(string? value)
    {
        Assert.Equal(CollectionViewMode.Grid, CollectionViewModePreference.Parse(value));
    }

    [Theory]
    [InlineData("list")]
    [InlineData("LIST")]
    public void ParseRestoresListValue(string value)
    {
        Assert.Equal(CollectionViewMode.List, CollectionViewModePreference.Parse(value));
    }

    [Theory]
    [InlineData(CollectionViewMode.Grid, "grid")]
    [InlineData(CollectionViewMode.List, "list")]
    public void SerializeUsesStableLocalValues(CollectionViewMode mode, string expected)
    {
        Assert.Equal(expected, CollectionViewModePreference.Serialize(mode));
    }
}
