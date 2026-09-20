using System.Globalization;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Lists every virtual desktop and switches to the pressed one. Entries carry the desktop UUID,
/// so renaming or reordering desktops never mis-targets a button.
/// </summary>
public sealed class VirtualDesktopFolderProvider : FolderProviderBase
{
    private static readonly PluginColor CurrentBackColor = new(0x20, 0x60, 0x30);
    private static readonly PluginColor BackColor = new(0x20, 0x20, 0x40);

    private readonly VirtualDesktopClient _desktops;
    private readonly KdeFolderGrid _grid;
    private readonly Func<bool> _showNames;

    private readonly Func<string, string> _translate;

    internal VirtualDesktopFolderProvider(
        VirtualDesktopClient desktops,
        KdeFolderGrid grid,
        Func<bool> showNames,
        Func<string, string> translate)
    {
        _desktops = desktops;
        _grid = grid;
        _showNames = showNames;
        _translate = translate;
    }

    // The host translates what a plugin declares; a folder title is built at runtime, so the
    // plugin looks it up itself against the same files.
    public override string Title => _translate("Virtual Desktops");

    public override void OnEnter() => _desktops.Changed += RaiseEntriesChanged;

    public override void OnExit() => _desktops.Changed -= RaiseEntriesChanged;

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        IReadOnlyList<VirtualDesktop> desktops = _desktops.Desktops;
        string currentId = _desktops.CurrentId;
        bool showNames = _showNames();

        List<FolderEntry> entries = new(desktops.Count);

        // Fill the slots in reading order, skipping the reserved back-button slot.
        for (int index = 0; index < desktops.Count; index++)
        {
            int slot = _grid.SlotForIndex(index);
            if (slot < 0)
            {
                break;
            }

            VirtualDesktop desktop = desktops[index];
            bool isCurrent = string.Equals(desktop.Id, currentId, StringComparison.Ordinal);
            string desktopId = desktop.Id;

            entries.Add(new FolderEntry
            {
                SlotIndex = slot,
                Text = showNames && desktop.Name.Length > 0
                    ? desktop.Name
                    : $"Desktop {desktop.Number.ToString(CultureInfo.InvariantCulture)}",
                BackColor = isCurrent ? CurrentBackColor : BackColor,
                Bold = isCurrent,
                OnPress = () => _desktops.SetCurrentAsync(desktopId)
            });
        }

        return entries;
    }
}
