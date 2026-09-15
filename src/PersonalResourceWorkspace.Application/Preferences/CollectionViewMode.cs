namespace PersonalResourceWorkspace.Application.Preferences;

public enum CollectionViewMode
{
    Grid,
    List,
}

public static class CollectionViewModePreference
{
    public const string GridValue = "grid";
    public const string ListValue = "list";

    public static CollectionViewMode Parse(string? value) =>
        string.Equals(value, ListValue, StringComparison.OrdinalIgnoreCase)
            ? CollectionViewMode.List
            : CollectionViewMode.Grid;

    public static string Serialize(CollectionViewMode mode) => mode switch
    {
        CollectionViewMode.List => ListValue,
        _ => GridValue,
    };
}
