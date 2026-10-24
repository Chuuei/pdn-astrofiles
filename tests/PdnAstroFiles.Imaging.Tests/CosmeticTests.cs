using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class CosmeticTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportBalancesSkyAndReducesGradientWhileKeepingEmissionAndSource(bool fits)
    {
        var source = LinearField(alpha: true);
        byte[] samples = source.RawData.ToArray();
        using var stream = new MemoryStream();
        if (fits) FitsWriter.Write(stream, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        else XisfWriter.Write(stream, [source]);
        byte[] file = stream.ToArray();
        stream.Position = 0;
        var set = (ScientificImageSet)(fits ? ScientificImageLoader.LoadFits(stream) : ScientificImageLoader.LoadXisf(stream));
        var display = Assert.Single(set);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.Automatic, display.Stretch);
        Assert.True(display.CosmeticsApplied);
        Assert.True(set.Metadata!.Images[0].CosmeticsApplied);
        Assert.Equal((160, 120), (display.Width, display.Height));
        Assert.Equal(samples, source.RawData.ToArray());
        Assert.Equal(file, stream.ToArray());
        Assert.InRange(TestImages.MedianLight(pixels), 0.16, 0.25);

        // The original per-channel sky pedestal spans .0014; after balancing the
        // background is neutral, while the red emission remains strongly colored.
        int background = (10 * 160 + 80) * 4, core = (60 * 160 + 80) * 4;
        Assert.InRange(Math.Abs(pixels[background] - pixels[background + 2]), 0, 0.005);
        Assert.True(pixels[core] > pixels[background] + 0.25);
        Assert.True(pixels[core] > pixels[core + 2] + 0.03, $"Emission red={pixels[core]}, blue={pixels[core + 2]}");

        // A coherent sky slope should flatten without removing the emission above.
        double left = Mean(pixels, 12, 20, 28, 100), right = Mean(pixels, 132, 20, 148, 100);
        int count = display.Width * display.Height;
        float[] raw = source.GetSamples<float>();
        // Remove this fixture's known additive color cast in the comparison, so
        // linked stretching does not confuse color balancing with slope reduction.
        for (int c = 0; c < 3; c++)
            for (int p = 0; p < count; p++) raw[c * count + p] -= c * 0.0007f;
        var baseline = XisfImageData.FromSamples(raw, [display.Width, display.Height], 4);
        baseline.DisplayFunction = XisfDisplayFunctions.CreateAdaptive(Enumerable.Range(0, 3)
            .Select(c => Enumerable.Range(0, count).Where(p => raw[3 * count + p] > 0).Select(p => (double)raw[c * count + p]).ToArray()).ToArray(),
            targetBackground: 0.20, clipping: -2.8, linkChannels: true);
        float[] unfinished = TestImages.Pixels(new ScientificImageDisplay(baseline));
        double originalSlope = Math.Abs(Mean(unfinished, 132, 20, 148, 100) - Mean(unfinished, 12, 20, 28, 100));
        Assert.True(Math.Abs(right - left) < originalSlope * 0.5, $"Sky slope: {originalSlope} -> {Math.Abs(right - left)}");
        for (int p = 0; p < display.Width * display.Height; p++)
            Assert.Equal(p % 160 < 4 ? 0f : p % 160 < 8 ? 0.5f : 1f, pixels[p * 4 + 3]);
    }

    [Fact]
    public void NoiseSmoothingReducesVarianceAndKeepsAStarAndSubBytePrecision()
    {
        const int width = 64, height = 48;
        var random = new Random(17);
        float[] rgba = new float[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            float value = 0.2f + (float)(random.NextDouble() - 0.5) * 0.02f;
            for (int c = 0; c < 3; c++) rgba[p * 4 + c] = value;
            rgba[p * 4 + 3] = 1;
        }
        int star = (24 * width + 32) * 4;
        for (int c = 0; c < 3; c++) rgba[star + c] = 0.9f;
        float[] result = Filter(rgba, width, height, 1, 0.006);
        var background = Enumerable.Range(0, width * height).Where(p => Math.Abs(p % width - 32) > 4 && Math.Abs(p / width - 24) > 4).ToArray();
        double before = Variance(background.Select(p => rgba[p * 4]));
        double after = Variance(background.Select(p => result[p * 4]));
        Assert.True(after < before * 0.4, $"Background variance: {before} -> {after}");
        Assert.InRange(result[star], 0.9f, 1);
        Assert.Contains(result, value => value > 0 && value < 1 && Math.Abs(value * 255 - Math.Round(value * 255)) > 0.1);
        Assert.All(Enumerable.Range(0, width * height), p => Assert.Equal(1f, result[p * 4 + 3]));
    }

    [Fact]
    public void DetailEnhancementAndColorBoostPreserveNeutralPixelsAndLightness()
    {
        const int width = 9, height = 9;
        float[] rgba = new float[width * height * 4];
        for (int p = 0; p < width * height; p++)
        {
            rgba[p * 4] = 0.5f; rgba[p * 4 + 1] = 0.3f; rgba[p * 4 + 2] = 0.2f; rgba[p * 4 + 3] = 1;
        }
        float[] color = Filter(rgba, width, height, 3, 0);
        Assert.True(color[0] > rgba[0]);
        Assert.True(color[2] < rgba[2]);
        Assert.Equal((rgba[0] + rgba[2]) / 2, (color[0] + color[2]) / 2, 6);

        for (int p = 0; p < width * height; p++)
            for (int c = 0; c < 3; c++) rgba[p * 4 + c] = p % width < 4 ? 0.2f : 0.6f;
        float[] detail = Filter(rgba, width, height, 3, 0);
        int edge = (4 * width + 4) * 4;
        Assert.True(detail[edge] > rgba[edge]);
        Assert.Equal(detail[edge], detail[edge + 1]);
        Assert.Equal(detail[edge], detail[edge + 2]);
        Assert.All(detail, value => Assert.InRange(value, 0, 1));
    }

    [Fact]
    public void InvalidAndInvisibleNeighborsDoNotBleedIntoOpaquePixels()
    {
        const int width = 32, height = 24;
        float[] rgba = Enumerable.Range(0, width * height * 4).Select(i => i % 4 == 3 ? 1f : 0.2f).ToArray();
        for (int p = 0; p < width * height; p++)
            if (p % width < 6) rgba[p * 4 + 3] = p % width < 4 ? 0 : 0.5f;
        float[] expected = Filter(rgba, width, height, 3, 0.01);
        for (int p = 0; p < width * height; p++)
            if (rgba[p * 4 + 3] == 0)
                for (int c = 0; c < 3; c++) rgba[p * 4 + c] = 1;
        // A single invalid sample is excluded from the neighborhood, not turned into
        // a dark value that could bias neighboring pixels during smoothing.
        int invalid = (10 * width + 16) * 4;
        rgba[invalid] = float.NaN;
        float[] actual = Filter(rgba, width, height, 3, 0.01);
        for (int p = 0; p < width * height; p++)
        {
            Assert.Equal(rgba[p * 4 + 3], actual[p * 4 + 3]);
            if (rgba[p * 4 + 3] > 0 && p * 4 != invalid)
                Assert.Equal(expected.AsSpan(p * 4, 4).ToArray(), actual.AsSpan(p * 4, 4).ToArray());
        }
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void CosmeticRowsAreDeterministicWhenRequestedInParallelAndAcrossBoundaries()
    {
        var display = new ScientificImageDisplay(LinearField());
        float[] expected = TestImages.Pixels(display), actual = new float[expected.Length];
        Parallel.For(0, display.Height, y => display.CopyRgbaRow(display.Height - 1 - y,
            actual.AsSpan((display.Height - 1 - y) * display.Width * 4, display.Width * 4)));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FinishingPreservesStoredOrientationAndResolutionAtFullSize()
    {
        var source = LinearField();
        source.Resolution = new(100, 200, "inch");
        float[] expected = TestImages.Pixels(new ScientificImageDisplay(source));
        source.Orientation = "90;flip";
        var rotated = new ScientificImageDisplay(source);
        float[] actual = TestImages.Pixels(rotated);
        Assert.Equal((120, 160), (rotated.Width, rotated.Height));
        Assert.Equal(200, rotated.HorizontalDpi);
        Assert.Equal(100, rotated.VerticalDpi);
        for (int y = 0; y < rotated.Height; y++)
        for (int x = 0; x < rotated.Width; x++)
        for (int c = 0; c < 4; c++)
            Assert.InRange(Math.Abs(actual[(y * rotated.Width + x) * 4 + c] -
                expected[((119 - x) * 160 + 159 - y) * 4 + c]), 0, 0.000001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrayscaleAndBayerAlphaRemainExactThroughFinishing(bool bayer)
    {
        const int width = 64, height = 48, count = width * height;
        float[] samples = new float[count * 2];
        for (int p = 0; p < count; p++)
        {
            samples[p] = 0.003f + 0.0006f * ((p * 37) % 101) / 100;
            samples[count + p] = p % 3 == 0 ? 0 : p % 3 == 1 ? 0.37f : 1;
        }
        var source = XisfImageData.FromSamples(samples, [width, height], 2);
        if (bayer) source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        byte[] original = source.RawData.ToArray();
        var display = new ScientificImageDisplay(source);
        float[] pixels = TestImages.Pixels(display);
        Assert.True(display.CosmeticsApplied);
        Assert.Equal(original, source.RawData.ToArray());
        for (int p = 0; p < count; p++)
        {
            Assert.Equal(samples[count + p], pixels[p * 4 + 3]);
            if (!bayer)
            {
                Assert.Equal(pixels[p * 4], pixels[p * 4 + 1]);
                Assert.Equal(pixels[p * 4], pixels[p * 4 + 2]);
            }
        }
        Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void NonFiniteSourceSamplesStaySafeDuringTheFullCosmeticImport()
    {
        var image = LinearField();
        float[] values = image.GetSamples<float>();
        for (int p = 0; p < 160 * 120; p += 593)
            values[p] = p % 2 == 0 ? float.NaN : float.PositiveInfinity;
        var source = XisfImageData.FromSamples(values, [160, 120], 3);
        byte[] original = source.RawData.ToArray();
        var display = new ScientificImageDisplay(source);
        Assert.True(display.CosmeticsApplied);
        Assert.All(TestImages.Pixels(display), value => Assert.True(float.IsFinite(value)));
        Assert.Equal(original, source.RawData.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SavedCosmeticPixelsAreNotProcessedAgainAndHistoryRetainsProvenance(bool fits)
    {
        var source = LinearField();
        source.Properties.Set("Observation:Object:Name", "Synthetic nebula");
        source.FitsKeywords.Add(XisfFitsKeyword.FromValue("OBJECT", "Synthetic nebula"));
        using var input = new MemoryStream();
        if (fits) FitsWriter.Write(input, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        else XisfWriter.Write(input, [source]);
        input.Position = 0;
        var loaded = (ScientificImageSet)(fits ? ScientificImageLoader.LoadFits(input) : ScientificImageLoader.LoadXisf(input));
        var metadata = AstronomyMetadata.Deserialize(loaded.Metadata!.Serialize());
        Assert.True(metadata.Images[0].CosmeticsApplied);
        float[] pixels = TestImages.Pixels(loaded[0]);
        using var output = new MemoryStream();
        AstronomyImageWriter.Save(output, metadata.Format, metadata,
            [new(metadata.Images[0].Id, loaded[0].Width, loaded[0].Height, pixels)], true);
        output.Position = 0;
        var reloaded = Assert.Single(fits ? ScientificImageLoader.LoadFits(output) : ScientificImageLoader.LoadXisf(output));
        Assert.False(reloaded.CosmeticsApplied);
        Assert.Equal(ImageStretch.None, reloaded.Stretch);
        Assert.Equal(pixels, TestImages.Pixels(reloaded));
        output.Position = 0;
        if (fits)
        {
            var header = FitsReader.Load(output).Hdus[0].Header;
            Assert.Equal("Synthetic nebula", header.GetRequiredValue<string>("OBJECT"));
            Assert.Contains(header, card => card.Keyword == "HISTORY" && card.Value?.ToString()?.Contains("cosmetics=True") == true);
        }
        else
        {
            var image = XisfReader.Load(output).Images[0];
            Assert.Equal("Synthetic nebula", image.Properties["Observation:Object:Name"].Value);
            Assert.Contains(image.FitsKeywords, card => card.Name == "HISTORY" && card.Comment?.Contains("cosmetics=True") == true);
        }
    }

    [Fact]
    public void StoredFunctionsDevelopedDataAndConstantFramesDoNotReceiveCosmetics()
    {
        var stored = LinearField();
        stored.DisplayFunction = TestImages.Stored();
        Assert.False(new ScientificImageDisplay(stored).CosmeticsApplied);
        Assert.False(new ScientificImageDisplay(TestImages.Developed()).CosmeticsApplied);
        Assert.False(new ScientificImageDisplay(XisfImageData.FromSamples(Enumerable.Repeat(0.003f, 128 * 96).ToArray(), [128, 96], 1)).CosmeticsApplied);
    }

    internal static XisfImageData LinearField(int width = 160, int height = 120, bool alpha = false)
    {
        int count = width * height;
        float[] samples = new float[count * (alpha ? 4 : 3)];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int p = y * width + x;
            double noise = 0.0001 * Math.Sin(x * 7.1 + y * 2.3);
            double gradient = 0.0005 * x / width;
            double nebula = 0.016 * Math.Exp(-(Math.Pow((x - width * 0.5) / (width * 0.1), 2) + Math.Pow((y - height * 0.5) / (height * 0.1), 2)));
            for (int c = 0; c < 3; c++)
                samples[c * count + p] = (float)(0.003 + c * 0.0007 + gradient + noise + nebula * (c == 0 ? 1 : 0.4));
            if (alpha) samples[3 * count + p] = x < 4 ? 0 : x < 8 ? 0.5f : 1;
        }
        return XisfImageData.FromSamples(samples, [width, height], alpha ? 4 : 3);
    }

    private static float[] Filter(float[] rgba, int width, int height, int colors, double noise)
    {
        float[] result = new float[rgba.Length];
        var filter = new ImportCosmetics(width, height, colors, noise,
            (int y, Span<float> row) => rgba.AsSpan(y * width * 4, width * 4).CopyTo(row));
        for (int y = 0; y < height; y++) filter.CopyRow(y, result.AsSpan(y * width * 4, width * 4));
        return result;
    }

    private static double Variance(IEnumerable<float> samples)
    {
        double[] values = samples.Select(value => (double)value).ToArray();
        double mean = values.Average();
        return values.Select(value => (value - mean) * (value - mean)).Average();
    }

    private static double Mean(float[] pixels, int x0, int y0, int x1, int y1) =>
        (from y in Enumerable.Range(y0, y1 - y0)
         from x in Enumerable.Range(x0, x1 - x0)
         select (double)pixels[(y * 160 + x) * 4]).Average();
}
