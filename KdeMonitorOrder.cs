namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Puts the monitors into the order the user configured. KWin lists its outputs in its own order,
/// which does not have to match how the monitors stand on the desk.
/// </summary>
internal static class KdeMonitorOrder
{
    /// <summary>
    /// Returns the outputs with the configured ones first, in the configured order, followed by
    /// everything else in the order KWin reported it. An entry that names no current output is
    /// ignored, so a monitor that is unplugged does not break the list.
    /// </summary>
    public static IReadOnlyList<BridgeOutput> Apply(IReadOnlyList<BridgeOutput> outputs, IReadOnlyList<string> order)
    {
        if (order.Count == 0 || outputs.Count == 0)
        {
            return outputs;
        }

        List<BridgeOutput> ordered = new(outputs.Count);

        foreach (string name in order)
        {
            foreach (BridgeOutput output in outputs)
            {
                if (string.Equals(output.Name, name, StringComparison.OrdinalIgnoreCase) && !ordered.Contains(output))
                {
                    ordered.Add(output);
                }
            }
        }

        foreach (BridgeOutput output in outputs)
        {
            if (!ordered.Contains(output))
            {
                ordered.Add(output);
            }
        }

        return ordered;
    }
}
