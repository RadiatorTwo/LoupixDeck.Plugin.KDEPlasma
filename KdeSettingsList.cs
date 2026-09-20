namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Reads the comma-separated list settings. The user types these by hand, so leading and trailing
/// whitespace and empty entries are ignored instead of becoming entries nothing ever matches.
/// </summary>
internal static class KdeSettingsList
{
    private static readonly char[] Separators = [',', ';', '\n'];

    public static IReadOnlyList<string> Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        List<string> entries = [];
        foreach (string part in value.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0)
            {
                entries.Add(trimmed);
            }
        }

        return entries;
    }
}
