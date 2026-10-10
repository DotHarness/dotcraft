namespace DotCraft.Imaging;

internal ref partial struct JpegDecoder
{
    private DecodedImage CreatePixels()
    {
        foreach (var component in _components)
        {
            var stride = component.Columns * 8;
            component.Samples = _budget.Allocate<byte>(ImageBudget.BufferLength(stride, component.Rows * 8, 1));
            var quantization = component.QuantValues ?? throw Invalid();
            for (var y = 0; y < component.Rows; y++)
                for (var x = 0; x < component.Columns; x++)
                    JpegTransform.Inverse(component.Coefficients.AsSpan((y * component.Columns + x) * 64, 64),
                        quantization, component.Samples.AsSpan(y * 8 * stride + x * 8), stride);
            _budget.Release((long)component.Coefficients.Length * sizeof(int));
            component.Coefficients = [];
        }
        var image = DecodedImage.Create(_width, _height, _budget);
        var directRgb = _components.Length == 3 && (_adobe == 0 || (_adobe < 0 && !_jfif && _components[0].Id == 'R' && _components[1].Id == 'G' && _components[2].Id == 'B'));
        if (_adobe > 2 || (_components.Length == 3 && _adobe == 2) || (_components.Length == 4 && _adobe == 1))
            throw new ImageCodecException(ImageError.UnsupportedFormat);
        Span<double> values = stackalloc double[4];
        for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < _components.Length; c++)
                    values[c] = Sample(_components[c], x, y);
                var offset = (y * _width + x) * 4;
                var r = values[0];
                var g = r;
                var b = r;
                if (_components.Length >= 3)
                {
                    if (directRgb || (_components.Length == 4 && _adobe != 2))
                    {
                        g = values[1];
                        b = values[2];
                    }
                    else
                    {
                        r = Math.Clamp(values[0] + 1.402 * (values[2] - 128), 0, 255);
                        g = Math.Clamp(values[0] - .344136 * (values[1] - 128) - .714136 * (values[2] - 128), 0, 255);
                        b = Math.Clamp(values[0] + 1.772 * (values[1] - 128), 0, 255);
                    }
                    if (_components.Length == 4)
                    {
                        if (_adobe == 2 || _adobe < 0)
                        {
                            r = 255 - r;
                            g = 255 - g;
                            b = 255 - b;
                        }
                        var k = _adobe < 0 ? 255 - values[3] : values[3];
                        r *= k / 255;
                        g *= k / 255;
                        b *= k / 255;
                    }
                }
                image.Pixels[offset] = JpegTransform.Clamp(r);
                image.Pixels[offset + 1] = JpegTransform.Clamp(g);
                image.Pixels[offset + 2] = JpegTransform.Clamp(b);
                image.Pixels[offset + 3] = 255;
            }
        foreach (var component in _components)
        {
            _budget.Release(component.Samples.Length);
            component.Samples = [];
        }
        return image with { Exif = _exif, IccProfile = CollectIcc() };
    }

    private readonly double Sample(JpegComponent component, int x, int y)
    {
        var sampleX = (x + .5) * component.Horizontal / _horizontal - .5;
        var sampleY = (y + .5) * component.Vertical / _vertical - .5;
        var floorX = (int)Math.Floor(sampleX);
        var floorY = (int)Math.Floor(sampleY);
        var left = Math.Clamp(floorX, 0, component.SampleWidth - 1);
        var right = Math.Clamp(floorX + 1, 0, component.SampleWidth - 1);
        var top = Math.Clamp(floorY, 0, component.SampleHeight - 1);
        var bottom = Math.Clamp(floorY + 1, 0, component.SampleHeight - 1);
        var weightX = sampleX - floorX;
        var weightY = sampleY - floorY;
        var stride = component.Columns * 8;
        var upper = component.Samples[top * stride + left] * (1 - weightX) + component.Samples[top * stride + right] * weightX;
        var lower = component.Samples[bottom * stride + left] * (1 - weightX) + component.Samples[bottom * stride + right] * weightX;
        return upper * (1 - weightY) + lower * weightY;
    }
}
