namespace DotCraft.Imaging;

internal ref partial struct JpegDecoder
{
    private void Scan(scoped ReadOnlySpan<byte> segment)
    {
        if (_components.Length == 0 || segment.Length < 6)
            throw Invalid();
        var count = segment[0];
        if (count == 0 || count > _components.Length || segment.Length != 4 + count * 2)
            throw Invalid();
        Span<int> componentIndices = stackalloc int[4];
        Span<int> dcTables = stackalloc int[4];
        Span<int> acTables = stackalloc int[4];
        for (var i = 0; i < count; i++)
        {
            var id = segment[1 + i * 2];
            var index = Array.FindIndex(_components, c => c.Id == id);
            if (index < 0 || componentIndices[..i].Contains(index))
                throw Invalid();
            componentIndices[i] = index;
            dcTables[i] = segment[2 + i * 2] >> 4;
            acTables[i] = segment[2 + i * 2] & 15;
            if (dcTables[i] > 3 || acTables[i] > 3)
                throw Invalid();
            var component = _components[index];
            component.Predictor = 0;
            var quantization = _quantization[component.Quantization] ?? throw Invalid();
            if (component.QuantValues is null)
            {
                component.QuantValues = _budget.Allocate<int>(64);
                quantization.CopyTo(component.QuantValues, 0);
            }
        }
        var start = segment[^3];
        var end = segment[^2];
        var high = segment[^1] >> 4;
        var low = segment[^1] & 15;
        if (start > end || end > 63 || high > 13 || low > 13)
            throw Invalid();
        if (!_progressive && (start != 0 || end != 63 || high != 0 || low != 0))
            throw Invalid();
        if (_progressive && ((start == 0 && end != 0) || (start > 0 && count != 1) || (high != 0 && high != low + 1)))
            throw Invalid();
        for (var i = 0; i < count; i++)
        {
            var component = _components[componentIndices[i]];
            for (var k = start; k <= end; k++)
            {
                if (component.Approximation[k] != (high == 0 ? -1 : high))
                    throw Invalid();
                component.Approximation[k] = low;
            }
            if (high == 0 && start == 0 && _huffman[0, dcTables[i]] is null)
                throw Invalid();
            if (end > 0 && _huffman[1, acTables[i]] is null)
                throw Invalid();
        }
        var single = _components[componentIndices[0]];
        var columns = count == 1 ? (single.SampleWidth + 7) / 8 : _mcuColumns;
        var rows = count == 1 ? (single.SampleHeight + 7) / 8 : _mcuRows;
        var reader = new JpegEntropyReader(_data, _position);
        var eobRun = 0;
        var restartNumber = 0;
        var total = columns * rows;
        for (var mcu = 0; mcu < total; mcu++)
        {
            if (_restart > 0 && mcu > 0 && mcu % _restart == 0)
            {
                if (eobRun != 0)
                    throw Invalid();
                reader.Restart(restartNumber);
                restartNumber = (restartNumber + 1) & 7;
                foreach (var component in _components)
                    component.Predictor = 0;
            }
            for (var i = 0; i < count; i++)
            {
                var component = _components[componentIndices[i]];
                var horizontal = count == 1 ? 1 : component.Horizontal;
                var vertical = count == 1 ? 1 : component.Vertical;
                for (var y = 0; y < vertical; y++)
                    for (var x = 0; x < horizontal; x++)
                    {
                        var blockX = mcu % columns * horizontal + x;
                        var blockY = mcu / columns * vertical + y;
                        var block = component.Coefficients.AsSpan((blockY * component.Columns + blockX) * 64, 64);
                        if (!_progressive)
                            Sequential(ref reader, component, block, _huffman[0, dcTables[i]]!, _huffman[1, acTables[i]]!);
                        else if (start == 0)
                            DcProgressive(ref reader, component, block, _huffman[0, dcTables[i]], high, low);
                        else if (high == 0)
                            AcFirst(ref reader, block, _huffman[1, acTables[i]]!, start, end, low, ref eobRun);
                        else
                            AcRefine(ref reader, block, _huffman[1, acTables[i]]!, start, end, low, ref eobRun);
                    }
            }
        }
        if (eobRun != 0)
            throw Invalid();
        reader.Finish();
        _position = reader.Position;
    }

    private static void Sequential(ref JpegEntropyReader reader, JpegComponent component, Span<int> block, JpegHuffman dc, JpegHuffman ac)
    {
        var size = dc.Read(ref reader);
        if (size > 11)
            throw Invalid();
        component.Predictor += reader.Signed(size);
        if (component.Predictor is < -2048 or > 2047)
            throw Invalid();
        block[0] = component.Predictor;
        var k = 1;
        while (k < 64)
        {
            var value = ac.Read(ref reader);
            var run = value >> 4;
            size = value & 15;
            if (size == 0)
            {
                if (run == 0)
                    break;
                if (run != 15 || k + 16 > 64)
                    throw Invalid();
                k += 16;
            }
            else
            {
                k += run;
                if (size > 10 || k >= 64)
                    throw Invalid();
                block[JpegTransform.ZigZag[k++]] = reader.Signed(size);
            }
        }
    }

    private static void DcProgressive(ref JpegEntropyReader reader, JpegComponent component, Span<int> block, JpegHuffman? dc, int high, int low)
    {
        if (high != 0)
        {
            block[0] |= reader.Bit() << low;
            return;
        }
        var size = dc!.Read(ref reader);
        if (size > 11)
            throw Invalid();
        component.Predictor += reader.Signed(size);
        var value = (long)component.Predictor << low;
        if (value is < -2048 or > 2047)
            throw Invalid();
        block[0] = (int)value;
    }

    private static void AcFirst(ref JpegEntropyReader reader, Span<int> block, JpegHuffman ac, int start, int end, int low, ref int eobRun)
    {
        if (eobRun > 0)
        {
            eobRun--;
            return;
        }
        var k = start;
        while (k <= end)
        {
            var value = ac.Read(ref reader);
            var run = value >> 4;
            var size = value & 15;
            if (size == 0)
            {
                if (run < 15)
                {
                    eobRun = (1 << run) + reader.Bits(run) - 1;
                    return;
                }
                if (k + 16 > end + 1)
                    throw Invalid();
                k += 16;
            }
            else
            {
                k += run;
                if (size > 10 || k > end)
                    throw Invalid();
                var coefficient = reader.Signed(size) << low;
                if (coefficient is < -32768 or > 32767)
                    throw Invalid();
                block[JpegTransform.ZigZag[k++]] = coefficient;
            }
        }
    }

    private static void AcRefine(ref JpegEntropyReader reader, Span<int> block, JpegHuffman ac, int start, int end, int low, ref int eobRun)
    {
        var k = start;
        var bit = 1 << low;
        if (eobRun == 0)
        {
            while (k <= end)
            {
                var value = ac.Read(ref reader);
                var run = value >> 4;
                var size = value & 15;
                var added = 0;
                if (size != 0)
                {
                    if (size != 1)
                        throw Invalid();
                    added = reader.Bit() != 0 ? bit : -bit;
                }
                else if (run != 15)
                {
                    eobRun = (1 << run) + reader.Bits(run);
                    break;
                }
                else
                    run = 16;
                while (k <= end)
                {
                    ref var coefficient = ref block[JpegTransform.ZigZag[k]];
                    if (coefficient != 0)
                        Refine(ref reader, ref coefficient, bit);
                    else
                    {
                        if (run == 0)
                            break;
                        run--;
                        if (run == 0 && added == 0)
                        {
                            k++;
                            break;
                        }
                    }
                    k++;
                }
                if (run != 0)
                    throw Invalid();
                if (added != 0)
                {
                    if (k > end)
                        throw Invalid();
                    block[JpegTransform.ZigZag[k++]] = added;
                }
            }
        }
        if (eobRun > 0)
        {
            for (; k <= end; k++)
            {
                ref var coefficient = ref block[JpegTransform.ZigZag[k]];
                if (coefficient != 0)
                    Refine(ref reader, ref coefficient, bit);
            }
            eobRun--;
        }
    }

    private static void Refine(ref JpegEntropyReader reader, ref int coefficient, int bit)
    {
        if (reader.Bit() != 0 && (Math.Abs(coefficient) & bit) == 0)
            coefficient += coefficient > 0 ? bit : -bit;
    }
}
