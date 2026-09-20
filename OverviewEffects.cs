namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// The Overview variants the user can pick in the settings. The stored values are public API
/// towards the settings file, so they must stay stable and are read case-insensitively.
/// </summary>
internal static class OverviewEffects
{
    public const string Overview = "overview";
    public const string Cycle = "cycle";
    public const string Grid = "grid";

    public const string OverviewAction = "Overview";
    public const string CycleAction = "Cycle Overview";
    public const string GridAction = "Grid View";

    /// <summary>Every accepted value, for the settings description.</summary>
    public static readonly string[] All = [Overview, Cycle, Grid];

    /// <summary>Maps a stored value to a KWin action name.</summary>
    public static string ActionFor(string effect) => effect switch
    {
        Cycle => CycleAction,
        Grid => GridAction,
        _ => OverviewAction
    };

    /// <summary>Turns whatever is in the settings file into one of the known values.</summary>
    public static string Normalize(string? effect)
    {
        if (string.IsNullOrWhiteSpace(effect))
        {
            return Overview;
        }

        string trimmed = effect.Trim();
        foreach (string known in All)
        {
            if (string.Equals(trimmed, known, StringComparison.OrdinalIgnoreCase))
            {
                return known;
            }
        }

        return Overview;
    }
}
