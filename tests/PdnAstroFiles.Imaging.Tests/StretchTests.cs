using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class StretchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DevelopedImageRetainsItsExposureEvenWithAStoredFunction(bool stored)
    {
        var source = TestImages.Developed();
        if (stored) source.DisplayFunction = TestImages.Stored();
        byte[] original = source.RawData.ToArray();
        var display = new ScientificImageDisplay(source);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.None, display.Stretch);
        Assert.Equal(original, source.RawData.ToArray());
        Assert.Equal(new[] { 0.02f, 0.1f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 0.98f },
            Enumerable.Range(0, 8).Select(p => pixels[p * 4]));
    }

    [Fact]
    public void LinearImageUsesStoredClippingAndMidtonesBeforeConsideringAutomaticStretch()
    {
        var source = TestImages.Linear(alpha: true);
        source.DisplayFunction = TestImages.Stored();
        byte[] original = source.RawData.ToArray();
        var display = new ScientificImageDisplay(source);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.Stored, display.Stretch);
        int count = display.Width * display.Height;
        float[] samples = source.GetSamples<float>();
        for (int p = 0; p < count; p++)
        {
            for (int c = 0; c < 3; c++)
            {
                double x = samples[c * count + p] / source.DisplayFunction.Highlights[c];
                double m = source.DisplayFunction.Midtones[c];
                double expected = (m - 1) * x / ((2 * m - 1) * x - m);
                Assert.Equal((float)expected, pixels[p * 4 + c], 6);
            }
            Assert.Equal(samples[3 * count + p], pixels[p * 4 + 3]);
        }
        Assert.Equal(original, source.RawData.ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.62)]
    public void AutomaticStretchTargetsDarkSkyAndRemovesHighPedestals(double pedestal)
    {
        var source = TestImages.Linear(pedestal, alpha: true);
        byte[] original = source.RawData.ToArray();
        var display = new ScientificImageDisplay(source);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.Automatic, display.Stretch);
        Assert.InRange(TestImages.MedianLight(pixels), 0.17, 0.24);
        Assert.Equal(64, display.Width);
        Assert.Equal(48, display.Height);
        for (int p = 0; p < pixels.Length / 4; p++)
        {
            Assert.Equal(p % 3 == 0 ? 0f : p % 3 == 1 ? 0.5f : 1f, pixels[p * 4 + 3]);
            Assert.True(pixels[p * 4] < pixels[p * 4 + 1]);
            Assert.True(pixels[p * 4 + 1] < pixels[p * 4 + 2]);
        }
        Assert.Equal(original, source.RawData.ToArray());
    }

    [Fact]
    public void FullyTransparentBrightPixelsDoNotChangeTheStretch()
    {
        var source = TestImages.Linear(alpha: true);
        float[] samples = source.GetSamples<float>();
        int count = source.Dimensions[0] * source.Dimensions[1];
        for (int p = 0; p < count; p += 3)
            for (int c = 0; c < 3; c++) samples[c * count + p] = 1;
        var changed = XisfImageData.FromSamples(samples, source.Dimensions, 4);
        float[] before = TestImages.Pixels(new ScientificImageDisplay(source));
        float[] after = TestImages.Pixels(new ScientificImageDisplay(changed));
        for (int p = 0; p < count; p++)
            if (p % 3 != 0)
                Assert.Equal(before.AsSpan(p * 4, 4).ToArray(), after.AsSpan(p * 4, 4).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.003)]
    [InlineData(0.8)]
    public void ConstantImagesAreNotArtificiallyStretched(double value)
    {
        var source = XisfImageData.FromSamples(Enumerable.Repeat(value, 16).ToArray(), [4, 4], 1);
        var display = new ScientificImageDisplay(source);
        Assert.Equal(ImageStretch.None, display.Stretch);
        Assert.Equal((float)value, TestImages.Pixels(display)[0]);
    }

    [Fact]
    public void NonFiniteSamplesRenderSafelyWithoutCorruptingStatistics()
    {
        var image = XisfImageData.FromSamples(new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0.003, 0.004, 0.005 }, [3, 2], 1);
        var display = new ScientificImageDisplay(image);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.Automatic, display.Stretch);
        Assert.All(pixels, value => Assert.True(float.IsFinite(value)));
        Assert.Equal(new[] { 0f, 0f, 0f }, Enumerable.Range(0, 3).Select(p => pixels[p * 4]));
    }

    [Fact]
    public void ImportDoesNotQuantizeToEightBits()
    {
        var image = XisfImageData.FromSamples(new[] { 0d, 0.2, 0.4, 0.400001, 0.6, 1 }, [3, 2], 1);
        var display = new ScientificImageDisplay(image);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(ImageStretch.None, display.Stretch);
        Assert.InRange(pixels[3 * 4] - pixels[2 * 4], 0.0000009f, 0.0000011f);
    }

    [Fact]
    public void IdentityStoredFunctionIsHonoredWithoutFallingBackToAutomatic()
    {
        var source = TestImages.Linear();
        source.DisplayFunction = new([0.5, 0.5, 0.5, 0.5], [0, 0, 0, 0], [1, 1, 1, 1], [0, 0, 0, 0], [1, 1, 1, 1]);
        var display = new ScientificImageDisplay(source);
        Assert.Equal(ImageStretch.Stored, display.Stretch);
        Assert.Equal(source.GetSamples<float>()[0], TestImages.Pixels(display)[0]);
    }
}
