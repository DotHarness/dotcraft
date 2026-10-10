namespace DotCraft.Imaging;

internal sealed class WebpVp8Header
{
    public bool Segmented { get; private set; }
    public bool UpdateMap { get; private set; }
    public bool AbsoluteSegments { get; private set; }
    public int[] SegmentQuantizers { get; } = new int[4];
    public int[] SegmentFilters { get; } = new int[4];
    public byte[] SegmentProbabilities { get; } = [255, 255, 255];
    public bool SimpleFilter { get; private set; }
    public int FilterLevel { get; private set; }
    public int Sharpness { get; private set; }
    public bool AdjustFilter { get; private set; }
    public int[] ReferenceDeltas { get; } = new int[4];
    public int[] ModeDeltas { get; } = new int[4];
    public int PartitionCount { get; private set; }
    public int Quantizer { get; private set; }
    public int[] QuantizerDeltas { get; } = new int[5];
    public byte[] Probabilities { get; }
    public bool MaySkip { get; private set; }
    public int SkipProbability { get; private set; }

    private WebpVp8Header(ImageBudget budget)
    {
        budget.Reserve(128);
        Probabilities = budget.Copy(WebpVp8Tables.CoefficientProbabilities);
    }

    public static WebpVp8Header Parse(WebpVp8Bits bits, ImageBudget budget)
    {
        var header = new WebpVp8Header(budget);
        if (bits.Read() != 0) throw new ImageCodecException(ImageError.UnsupportedFormat);
        _ = bits.Read();
        header.Segmented = bits.Read() != 0;
        if (header.Segmented)
        {
            header.UpdateMap = bits.Read() != 0;
            if (bits.Read() != 0)
            {
                header.AbsoluteSegments = bits.Read() != 0;
                for (var i = 0; i < 4; i++) header.SegmentQuantizers[i] = bits.OptionalSigned(7);
                for (var i = 0; i < 4; i++) header.SegmentFilters[i] = bits.OptionalSigned(6);
            }
            if (header.UpdateMap)
                for (var i = 0; i < 3; i++)
                    if (bits.Read() != 0) header.SegmentProbabilities[i] = (byte)bits.Literal(8);
        }
        header.SimpleFilter = bits.Read() != 0;
        header.FilterLevel = bits.Literal(6);
        header.Sharpness = bits.Literal(3);
        header.AdjustFilter = bits.Read() != 0;
        if (header.AdjustFilter && bits.Read() != 0)
        {
            for (var i = 0; i < 4; i++)
                if (bits.Read() != 0) header.ReferenceDeltas[i] = bits.Signed(6);
            for (var i = 0; i < 4; i++)
                if (bits.Read() != 0) header.ModeDeltas[i] = bits.Signed(6);
        }
        header.PartitionCount = 1 << bits.Literal(2);
        header.Quantizer = bits.Literal(7);
        for (var i = 0; i < 5; i++) header.QuantizerDeltas[i] = bits.OptionalSigned(4);
        _ = bits.Read();
        var update = WebpVp8Tables.CoefficientUpdateProbabilities;
        for (var i = 0; i < header.Probabilities.Length; i++)
            if (bits.Read(update[i]) != 0) header.Probabilities[i] = (byte)bits.Literal(8);
        header.MaySkip = bits.Read() != 0;
        if (header.MaySkip) header.SkipProbability = bits.Literal(8);
        return header;
    }

    public int GetQuantizer(int segment) => Math.Clamp(Segmented
        ? SegmentQuantizers[segment] + (AbsoluteSegments ? 0 : Quantizer) : Quantizer, 0, 127);

    public int GetFilter(int segment, bool blockPrediction)
    {
        var level = Segmented ? SegmentFilters[segment] + (AbsoluteSegments ? 0 : FilterLevel) : FilterLevel;
        level = Math.Clamp(level, 0, 63);
        if (AdjustFilter) level += ReferenceDeltas[0] + (blockPrediction ? ModeDeltas[0] : 0);
        return Math.Clamp(level, 0, 63);
    }
}
