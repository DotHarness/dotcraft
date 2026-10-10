namespace DotCraft.Agents;

internal static class ModelImageSizing
{
    private const int MaxDimension = 2048;
    private const int PatchSize = 32;
    private const int MaxPatches = 2500;

    public static (int Width, int Height) Fit(int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions must be positive.");
        if (Fits(width, height))
            return (width, height);

        var scale = Math.Min(1, (double)MaxDimension / Math.Max(width, height));
        width = Math.Max(1, (int)Math.Round(width * scale, MidpointRounding.AwayFromZero));
        height = Math.Max(1, (int)Math.Round(height * scale, MidpointRounding.AwayFromZero));
        if (Fits(width, height))
            return (width, height);

        scale = Math.Sqrt((double)PatchSize * PatchSize * MaxPatches / width / height);
        var patchesWide = width * scale / PatchSize;
        var patchesHigh = height * scale / PatchSize;
        scale *= Math.Min(Math.Floor(patchesWide) / patchesWide, Math.Floor(patchesHigh) / patchesHigh);
        return (Math.Max(1, (int)Math.Floor(width * scale)), Math.Max(1, (int)Math.Floor(height * scale)));
    }

    private static bool Fits(int width, int height) =>
        width <= MaxDimension && height <= MaxDimension
        && ((width + PatchSize - 1L) / PatchSize) * ((height + PatchSize - 1L) / PatchSize) <= MaxPatches;
}
