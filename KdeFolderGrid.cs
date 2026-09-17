using System.Runtime.CompilerServices;
using LoupixDeck.PluginSdk;

namespace LoupixDeck.Plugin.KDEPlasma;

/// <summary>
/// Plugin-owned mirror of the SDK's <c>FolderGridInfo</c>, with the same fields and the same
/// <see cref="SlotForIndex"/> algorithm. Kept as a separate type so no provider or command in
/// this plugin ever mentions the SDK type in a signature — see <see cref="KdeFolderGridResolver"/>.
/// </summary>
internal sealed record KdeFolderGrid(int Columns, int Rows, int BackSlotIndex)
{
    /// <summary>Addressable slots — entries outside this range are dropped by the host.</summary>
    public int TotalSlots => Columns * Rows;

    /// <summary>
    /// Fills the grid in reading order, skipping the reserved back slot, and stops when the
    /// grid is full. Returns the slot index for the n-th entry, or -1 when it does not fit.
    /// </summary>
    public int SlotForIndex(int entryIndex)
    {
        int slot = 0;
        for (int i = 0; i <= entryIndex; i++)
        {
            if (slot == BackSlotIndex)
            {
                slot++;
            }

            if (slot >= TotalSlots)
            {
                return -1;
            }

            if (i == entryIndex)
            {
                return slot;
            }

            slot++;
        }

        return -1;
    }
}

/// <summary>
/// Resolves the active device's folder grid with a fallback for hosts built against an older SDK.
/// A host loads this plugin whenever the SDK major version matches, so it may run against an SDK
/// assembly that has neither <c>IPluginHost.FolderGrid</c> nor <c>FolderGridInfo</c>. Merely
/// jitting a method whose signature mentions <c>FolderGridInfo</c> throws there, so the SDK type
/// appears in exactly one non-inlined method that converts it to the plugin-owned type.
/// </summary>
internal static class KdeFolderGridResolver
{
    /// <summary>Falls back to the 5x3 layout described by <see cref="FolderLayout"/>.</summary>
    private static readonly KdeFolderGrid Fallback =
        new(FolderLayout.Columns, FolderLayout.TotalSlots / FolderLayout.Columns, FolderLayout.BackSlotIndex);

    public static KdeFolderGrid Resolve(IPluginHost host)
    {
        try
        {
            return ReadFolderGrid(host);
        }
        catch (MissingMethodException)
        {
            return Fallback;
        }
        catch (TypeLoadException)
        {
            return Fallback;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static KdeFolderGrid ReadFolderGrid(IPluginHost host)
    {
        FolderGridInfo grid = host.FolderGrid;
        return new KdeFolderGrid(grid.Columns, grid.Rows, grid.BackSlotIndex);
    }
}
