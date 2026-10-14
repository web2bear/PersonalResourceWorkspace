namespace PersonalResourceWorkspace.Application.Preferences;

public enum ResourceViewMode
{
    GridSmall,
    Grid,
    GridLarge,
    List,
}

public static class ResourceViewModePreference
{
    public const string GridValue = "grid";
    public const string GridMediumValue = "grid-medium";
    public const string GridSmallValue = "grid-small";
    public const string GridLargeValue = "grid-large";
    public const string ListValue = "list";

    public static ResourceViewMode Parse(string? value) => value?.ToLowerInvariant() switch
    {
        GridSmallValue => ResourceViewMode.GridSmall,
        GridLargeValue => ResourceViewMode.GridLarge,
        ListValue => ResourceViewMode.List,
        _ => ResourceViewMode.Grid,
    };

    public static string Serialize(ResourceViewMode mode) => mode switch
    {
        ResourceViewMode.List => ListValue,
        ResourceViewMode.GridSmall => GridSmallValue,
        ResourceViewMode.GridLarge => GridLargeValue,
        _ => GridMediumValue,
    };
}
