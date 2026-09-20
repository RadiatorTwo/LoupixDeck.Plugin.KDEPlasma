using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Lists every Activity and switches to the pressed one. Entries carry the Activity UUID, so a
/// renamed Activity keeps working.
/// </summary>
public sealed class ActivityFolderProvider : FolderProviderBase
{
    private static readonly PluginColor CurrentBackColor = new(0x20, 0x60, 0x30);
    private static readonly PluginColor BackColor = new(0x20, 0x20, 0x40);

    private readonly ActivityManagerClient _activities;
    private readonly KdeFolderGrid _grid;
    private readonly Func<KdeActivity, bool> _isHidden;

    internal ActivityFolderProvider(ActivityManagerClient activities, KdeFolderGrid grid, Func<KdeActivity, bool> isHidden)
    {
        _activities = activities;
        _grid = grid;
        _isHidden = isHidden;
    }

    public override string Title => "Activities";

    public override void OnEnter() => _activities.Changed += RaiseEntriesChanged;

    public override void OnExit() => _activities.Changed -= RaiseEntriesChanged;

    public override IReadOnlyList<FolderEntry> BuildEntries()
    {
        // The hidden ones are filtered first, so hiding one does not leave a gap in the grid.
        List<KdeActivity> activities = [];
        foreach (KdeActivity candidate in _activities.Activities)
        {
            if (!_isHidden(candidate))
            {
                activities.Add(candidate);
            }
        }

        string currentId = _activities.CurrentId;

        List<FolderEntry> entries = new(activities.Count);

        // Fill the slots in reading order, skipping the reserved back-button slot.
        for (int index = 0; index < activities.Count; index++)
        {
            int slot = _grid.SlotForIndex(index);
            if (slot < 0)
            {
                break;
            }

            KdeActivity activity = activities[index];
            bool isCurrent = string.Equals(activity.Id, currentId, StringComparison.Ordinal);
            string activityId = activity.Id;

            // KDE reports icons as icon theme names, which the SDK cannot render, so entries stay text only.
            entries.Add(new FolderEntry
            {
                SlotIndex = slot,
                Text = activity.Name,
                BackColor = isCurrent ? CurrentBackColor : BackColor,
                Bold = isCurrent,
                OnPress = () => _activities.SetCurrentAsync(activityId)
            });
        }

        return entries;
    }
}
