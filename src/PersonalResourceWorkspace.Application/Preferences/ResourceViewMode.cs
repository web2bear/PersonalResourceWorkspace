namespace PersonalResourceWorkspace.Application.Preferences;

public enum ResourceViewMode
{
    Grid,
    List,
}

public static class ResourceViewModePreference
{
    public const string GridValue = "grid";
    public const string ListValue = "list";

    public static ResourceViewMode Parse(string? value) =>
        string.Equals(value, ListValue, StringComparison.OrdinalIgnoreCase)
            ? ResourceViewMode.List
            : ResourceViewMode.Grid;

    public static string Serialize(ResourceViewMode mode) => mode switch
    {
        ResourceViewMode.List => ListValue,
        _ => GridValue,
    };
}
