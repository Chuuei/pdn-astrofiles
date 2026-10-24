using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class DebayerTests
{
    [Theory]
    [InlineData("RGGB", false)]
    [InlineData("BGGR", false)]
    [InlineData("GRBG", false)]
    [InlineData("GBRG", false)]
    [InlineData("RGGB", true)]
    [InlineData("BGGR", true)]
    [InlineData("GRBG", true)]
    [InlineData("GBRG", true)]
    public void RecordedPatternsProduceFullResolutionRgbInBothFormats(string pattern, bool fits)
    {
        var source = Mosaic(pattern);
        if (fits) source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", pattern));
        else source.ColorFilterArray = new(pattern, 2, 2, "Bayer");
        using var input = new MemoryStream();
        if (fits) FitsWriter.Write(input, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        else XisfWriter.Write(input, [source]);
        byte[] original = input.ToArray();
        input.Position = 0;
        var display = Assert.Single(fits ? ScientificImageLoader.LoadFits(input) : ScientificImageLoader.LoadXisf(input));
        Assert.Equal(pattern, display.BayerPattern);
        Assert.Equal((7, 5), (display.Width, display.Height));
        AssertColor(display);
        Assert.Equal(original, input.ToArray());
    }

    [Theory]
    [InlineData(0, 0, "RGGB")]
    [InlineData(1, 0, "GRBG")]
    [InlineData(0, 1, "GBRG")]
    [InlineData(1, 1, "BGGR")]
    [InlineData(-1, -1, "BGGR")]
    public void FitsOffsetsAndHierarchAliasesResolveTheStoredTile(int x, int y, string effective)
    {
        var source = Mosaic(effective);
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("HIERARCH BAYERPAT", "rggb"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("HIERARCH XBAYEROFF", x));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("HIERARCH YBAYEROFF", y));
        var display = new ScientificImageDisplay(source);
        Assert.Equal(effective, display.BayerPattern);
        AssertColor(display);
    }

    [Theory]
    [InlineData("TOP-DOWN", 6, "RGGB")]
    [InlineData("BOTTOM-UP", 6, "GBRG")]
    [InlineData("BOTTOM-UP", 5, "RGGB")]
    public void FitsRowOrderAdjustsPatternParityWithoutFlippingTheData(string order, int height, string effective)
    {
        var source = Mosaic(effective, height: height);
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", order));
        var display = new ScientificImageDisplay(source);
        Assert.Equal(effective, display.BayerPattern);
        AssertColor(display);
    }

    [Fact]
    public void NativeCfaTakesPrecedenceOverContradictoryFitsMetadata()
    {
        var source = Mosaic("GBRG");
        source.ColorFilterArray = new("GBRG", 2, 2, "Bayer");
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("XBAYROFF", "invalid"));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", "BOTTOM-UP"));
        var display = new ScientificImageDisplay(source);
        Assert.Equal("GBRG", display.BayerPattern);
        AssertColor(display);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unsupported")]
    [InlineData("offset")]
    [InlineData("row-order")]
    [InlineData("native")]
    public void MissingOrInvalidMetadataLeavesTheImageGrayscale(string kind)
    {
        var source = Mosaic("RGGB");
        if (kind != "missing") source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", kind == "unsupported" ? "RGBG" : "RGGB"));
        if (kind == "offset") source.FitsKeywords.Add(XisfFitsKeyword.FromValue("YBAYROFF", 0.5));
        if (kind == "row-order") source.FitsKeywords.Add(XisfFitsKeyword.FromValue("ROWORDER", "unknown"));
        if (kind == "native") source.ColorFilterArray = new("RGBG", 2, 2, "Bayer");
        var display = new ScientificImageDisplay(source);
        Assert.Null(display.BayerPattern);
        float[] pixels = TestImages.Pixels(display);
        for (int p = 0; p < pixels.Length; p += 4)
        {
            Assert.Equal(pixels[p], pixels[p + 1]);
            Assert.Equal(pixels[p], pixels[p + 2]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AlreadyRgbOrRgbaImagesIgnoreStaleBayerMetadata(bool alpha)
    {
        var source = TestImages.Linear(alpha: alpha);
        float[] expected = TestImages.Pixels(new ScientificImageDisplay(source));
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
        source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        var display = new ScientificImageDisplay(source);
        Assert.Null(display.BayerPattern);
        Assert.Equal(expected, TestImages.Pixels(display));
    }

    [Theory]
    [InlineData(XisfSampleFormat.UInt8, false)]
    [InlineData(XisfSampleFormat.UInt16, true)]
    [InlineData(XisfSampleFormat.UInt32, false)]
    [InlineData(XisfSampleFormat.UInt64, true)]
    [InlineData(XisfSampleFormat.Float32, true)]
    [InlineData(XisfSampleFormat.Float64, false)]
    public void DebayeringHandlesRealSampleFormatsAndByteOrder(XisfSampleFormat format, bool big)
    {
        byte[] values = Enumerable.Range(0, 35).Select(p => "RGGB"[((p / 7) & 1) * 2 + ((p % 7) & 1)] switch
        {
            'R' => (byte)204, 'G' => (byte)102, _ => (byte)51
        }).ToArray();
        XisfImageData source = format switch
        {
            XisfSampleFormat.UInt8 => XisfImageData.FromSamples(values, [7, 5], 1),
            XisfSampleFormat.UInt16 => XisfImageData.FromSamples(values.Select(v => (ushort)(v * 257)).ToArray(), [7, 5], 1, isBigEndian: big),
            XisfSampleFormat.UInt32 => XisfImageData.FromSamples(values.Select(v => v * (uint.MaxValue / 255)).ToArray(), [7, 5], 1, isBigEndian: big),
            XisfSampleFormat.UInt64 => XisfImageData.FromSamples(values.Select(v => v * (ulong.MaxValue / 255)).ToArray(), [7, 5], 1, isBigEndian: big),
            XisfSampleFormat.Float32 => XisfImageData.FromSamples(values.Select(v => v / 255f).ToArray(), [7, 5], 1, isBigEndian: big),
            _ => XisfImageData.FromSamples(values.Select(v => v / 255d).ToArray(), [7, 5], 1, isBigEndian: big)
        };
        source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        byte[] original = source.RawData.ToArray();
        AssertColor(new ScientificImageDisplay(source));
        Assert.Equal(original, source.RawData.ToArray());
    }

    [Fact]
    public void BilinearNeighborsIncludeEdgesAndKeepMeasuredSamples()
    {
        var source = XisfImageData.FromSamples(new byte[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 }, [3, 3], 1);
        source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        var display = new ScientificImageDisplay(source);
        Assert.Equal(ImageStretch.None, display.Stretch);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(new[] { 10 / 255f, 30 / 255f, 50 / 255f, 1 }, pixels[..4]);
        Assert.Equal(new[] { 50 / 255f, 50 / 255f, 50 / 255f, 1 }, pixels[16..20]);
        Assert.Equal(new[] { 90 / 255f, 70 / 255f, 50 / 255f, 1 }, pixels[32..36]);
    }

    [Fact]
    public void ReconstructionRunsBeforeOrientationAndRetainsResolution()
    {
        var source = Mosaic("GRBG");
        source.ColorFilterArray = new("GRBG", 2, 2, "Bayer");
        source.Orientation = "90;flip";
        source.Resolution = new(100, 200, "inch");
        var display = new ScientificImageDisplay(source);
        Assert.Equal((5, 7), (display.Width, display.Height));
        Assert.Equal(200, display.HorizontalDpi);
        Assert.Equal(100, display.VerticalDpi);
        AssertColor(display);
    }

    [Fact]
    public void StoredGrayStretchIsLinkedAcrossTheReconstructedColors()
    {
        var source = Mosaic("RGGB", linear: true);
        source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        source.DisplayFunction = new([0.2, 0.5, 0.5, 0.5], [0, 0, 0, 0], [1, 1, 1, 1], [0, 0, 0, 0], [1, 1, 1, 1]);
        var display = new ScientificImageDisplay(source);
        Assert.Equal(ImageStretch.Stored, display.Stretch);
        float[] pixels = TestImages.Pixels(display);
        foreach (int c in Enumerable.Range(0, 3))
        {
            // At the top-left, green averages rows zero and one; blue comes from row one.
            double x = c == 0 ? 0.003f : c == 1 ? 0.004005f : 0.00501f;
            double expected = -0.8 * x / (-0.6 * x - 0.2);
            Assert.Equal((float)expected, pixels[c], 6);
        }
    }

    [Fact]
    public void LinearMosaicUsesTheBrighterAutomaticTargetAfterReconstruction()
    {
        // A neutral noisy sky gives comparable RGB medians after interpolation.
        float[] samples = Enumerable.Range(0, 64 * 48)
            .Select(p => 0.003f + 0.0006f * ((p * 37) % 101) / 100).ToArray();
        var source = XisfImageData.FromSamples(samples, [64, 48], 1);
        source.ColorFilterArray = new("BGGR", 2, 2, "Bayer");
        var display = new ScientificImageDisplay(source);
        Assert.Equal(ImageStretch.Automatic, display.Stretch);
        Assert.InRange(TestImages.MedianLight(TestImages.Pixels(display)), 0.15, 0.25);
    }

    private static XisfImageData Mosaic(string pattern, int width = 7, int height = 5, bool linear = false)
    {
        float[] pixels = Enumerable.Range(0, width * height).Select(p =>
        {
            char c = pattern[((p / width) & 1) * 2 + (p % width & 1)];
            return linear
                ? (c == 'R' ? 0.003f : c == 'G' ? 0.004f : 0.005f) + (p / width) * 0.00001f
                : c == 'R' ? 0.8f : c == 'G' ? 0.4f : 0.2f;
        }).ToArray();
        return XisfImageData.FromSamples(pixels, [width, height], 1);
    }

    private static void AssertColor(ScientificImageDisplay display)
    {
        float[] pixels = TestImages.Pixels(display);
        for (int p = 0; p < pixels.Length; p += 4)
        {
            Assert.Equal(0.8f, pixels[p], 6);
            Assert.Equal(0.4f, pixels[p + 1], 6);
            Assert.Equal(0.2f, pixels[p + 2], 6);
            Assert.Equal(1f, pixels[p + 3]);
        }
    }
}
