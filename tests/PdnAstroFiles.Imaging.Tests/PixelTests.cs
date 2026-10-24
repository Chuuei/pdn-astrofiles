using System.Buffers.Binary;
using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class PixelTests
{
    [Theory]
    [InlineData(false, XisfPixelLayout.Planar)]
    [InlineData(true, XisfPixelLayout.Planar)]
    [InlineData(false, XisfPixelLayout.Interleaved)]
    [InlineData(true, XisfPixelLayout.Interleaved)]
    public void UInt16RgbaReadsTheDeclaredLayoutAndByteOrder(bool big, XisfPixelLayout layout)
    {
        ushort[][] values = [[0, 16384, 49152, 65535], [65535, 49152, 16384, 0], [32768, 10000, 50000, 20000], [65535, 32768, 0, 65535]];
        byte[] raw = new byte[32];
        for (int p = 0; p < 4; p++)
        for (int c = 0; c < 4; c++)
        {
            int offset = (layout == XisfPixelLayout.Planar ? c * 4 + p : p * 4 + c) * 2;
            if (big) BinaryPrimitives.WriteUInt16BigEndian(raw.AsSpan(offset), values[c][p]);
            else BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(offset), values[c][p]);
        }
        var display = new ScientificImageDisplay(XisfImageData.FromRaw(raw, [2, 2], 4, XisfSampleFormat.UInt16, layout, big));
        Assert.Equal(ImageStretch.None, display.Stretch);
        float[] pixels = TestImages.Pixels(display);
        for (int p = 0; p < 4; p++)
        for (int c = 0; c < 4; c++)
            Assert.Equal(values[c][p] / 65535f, pixels[p * 4 + c], 6);
    }

    [Theory]
    [InlineData("0", 2, 3, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData("flip", 2, 3, new[] { 1, 0, 3, 2, 5, 4 })]
    [InlineData("90", 3, 2, new[] { 1, 3, 5, 0, 2, 4 })]
    [InlineData("-90", 3, 2, new[] { 4, 2, 0, 5, 3, 1 })]
    [InlineData("180", 2, 3, new[] { 5, 4, 3, 2, 1, 0 })]
    [InlineData("90;flip", 3, 2, new[] { 5, 3, 1, 4, 2, 0 })]
    public void OrientationIsAppliedAtFullResolution(string orientation, int width, int height, int[] indices)
    {
        byte[] values = [10, 40, 90, 140, 200, 250];
        var source = XisfImageData.FromSamples(values, [2, 3], 1);
        source.Orientation = orientation;
        source.Resolution = new(100, 200, "cm");
        var display = new ScientificImageDisplay(source);
        Assert.Equal(width, display.Width);
        Assert.Equal(height, display.Height);
        Assert.Equal((width == 3 ? 200 : 100) * 2.54, display.HorizontalDpi);
        float[] pixels = TestImages.Pixels(display);
        for (int p = 0; p < 6; p++) Assert.Equal(values[indices[p]] / 255f, pixels[p * 4], 6);
    }

    [Fact]
    public void UInt64BoundsAreNormalizedBeforeStretchDetection()
    {
        var image = XisfImageData.FromSamples(new[] { 0UL, ulong.MaxValue / 4, ulong.MaxValue / 2, ulong.MaxValue }, [2, 2], 1, isBigEndian: true);
        var display = new ScientificImageDisplay(image);
        Assert.Equal(ImageStretch.None, display.Stretch);
        float[] pixels = TestImages.Pixels(display);
        Assert.Equal(new[] { 0f, 0.25f, 0.5f, 1f }, Enumerable.Range(0, 4).Select(p => pixels[p * 4]));
    }

    [Fact]
    public void ExplicitSampleBoundsAreRespected()
    {
        var source = XisfImageData.FromSamples(new[] { 100d, 200, 400, 500 }, [2, 2], 1);
        source.LowerBound = 100;
        source.UpperBound = 500;
        var display = new ScientificImageDisplay(source);
        Assert.Equal(new[] { 0f, 0.25f, 0.75f, 1f }, Enumerable.Range(0, 4).Select(p => TestImages.Pixels(display)[p * 4]));
    }

    [Fact]
    public void UnsupportedScientificVolumesAndComplexSamplesAreRejected()
    {
        var volume = XisfImageData.FromSamples(new float[8], [2, 2, 2], 1);
        Assert.Throws<NotSupportedException>(() => new ScientificImageDisplay(volume));
        var complex = XisfImageData.FromSamples(new System.Numerics.Complex[4], [2, 2], 1);
        Assert.Throws<NotSupportedException>(() => new ScientificImageDisplay(complex));
        var badBounds = TestImages.Developed();
        badBounds.LowerBound = 1;
        badBounds.UpperBound = 1;
        Assert.Throws<NotSupportedException>(() => new ScientificImageDisplay(badBounds));
    }
}
