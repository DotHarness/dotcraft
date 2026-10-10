namespace DotCraft.Imaging;

internal readonly struct PngCompressionReservation : IDisposable
{
    private const int WorkspaceBytes = 512 * 1024;
    private readonly ImageBudget _budget;

    public PngCompressionReservation(ImageBudget budget)
    {
        // Covers the BCL zlib stream's native state, window, tables and staging buffers.
        budget.Reserve(WorkspaceBytes);
        _budget = budget;
    }

    public void Dispose() => _budget.Release(WorkspaceBytes);
}
