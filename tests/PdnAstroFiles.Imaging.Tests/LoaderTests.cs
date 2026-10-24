using System.Buffers.Binary;
using System.Text;
using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

public sealed class LoaderTests
{
    [Theory]
    [InlineData(XisfCompression.None)]
    [InlineData(XisfCompression.Zlib)]
    [InlineData(XisfCompression.Lz4)]
    [InlineData(XisfCompression.Zstd)]
    public void XisfStoredFunctionSurvivesDecodingAndInputRemainsOpen(XisfCompression compression)
    {
        var source = TestImages.Linear(alpha: true);
        source.Id = "Target";
        source.DisplayFunction = TestImages.Stored();
        using var input = new MemoryStream();
        XisfWriter.Write(input, [source], new() { Compression = compression, Shuffle = true });
        byte[] original = input.ToArray();
        input.Position = 0;
        var loaded = Assert.Single(ScientificImageLoader.LoadXisf(input));
        Assert.Equal(ImageStretch.Stored, loaded.Stretch);
        Assert.Equal("Target", loaded.Name);
        Assert.Equal(TestImages.Pixels(new ScientificImageDisplay(source)), TestImages.Pixels(loaded));
        Assert.True(input.CanRead);
        Assert.Equal(original, input.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FitsRgbAndRgbaMatchXisfWithIdenticalStretchBehavior(bool alpha)
    {
        var source = TestImages.Linear(alpha: alpha);
        using var fits = new MemoryStream();
        FitsWriter.Write(fits, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        byte[] original = fits.ToArray();
        fits.Position = 0;
        var loaded = Assert.Single(ScientificImageLoader.LoadFits(fits));
        Assert.Equal(ImageStretch.Automatic, loaded.Stretch);
        Assert.Equal(TestImages.Pixels(new ScientificImageDisplay(source)), TestImages.Pixels(loaded));
        Assert.True(fits.CanRead);
        Assert.Equal(original, fits.ToArray());
    }

    [Fact]
    public void IndependentlyEncodedFitsAppliesScalingAndBlankBeforeDisplay()
    {
        using var input = MakeFits(
            [short.MinValue, 0, 50, 100, 150],
            ["BSCALE  =                    2", "BZERO   =                  100", "BLANK   =               -32768"]);
        var display = Assert.Single(ScientificImageLoader.LoadFits(input));
        Assert.Equal(ImageStretch.None, display.Stretch);
        float[] rgba = TestImages.Pixels(display);
        Assert.Equal(new[] { 0f, 0f, 1f / 3, 2f / 3, 1f }, Enumerable.Range(0, 5).Select(p => rgba[p * 4]));
    }

    [Fact]
    public void UnsignedFitsBzeroUsesTheFullUnsignedRange()
    {
        using var input = MakeFits([short.MinValue, -16384, 16384, short.MaxValue], ["BZERO   =                32768"]);
        var display = Assert.Single(ScientificImageLoader.LoadFits(input));
        Assert.Equal(ImageStretch.None, display.Stretch);
        float[] rgba = TestImages.Pixels(display);
        Assert.Equal(new[] { 0f, 16384 / 65535f, 49152 / 65535f, 1f }, Enumerable.Range(0, 4).Select(p => rgba[p * 4]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContainersRetainMultipleImagesAndTheirOriginalSizes(bool fits)
    {
        var first = TestImages.Developed();
        var second = XisfImageData.FromSamples(new byte[] { 0, 80, 170, 255 }, [2, 2], 1);
        using var input = new MemoryStream();
        if (fits) FitsWriter.Write(input, FitsConverter.FromXisf([first, second], ConversionLossPolicy.Allow));
        else XisfWriter.Write(input, [first, second]);
        input.Position = 0;
        var images = fits ? ScientificImageLoader.LoadFits(input) : ScientificImageLoader.LoadXisf(input);
        Assert.Equal(2, images.Count);
        Assert.Equal((4, 2), (images[0].Width, images[0].Height));
        Assert.Equal((2, 2), (images[1].Width, images[1].Height));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonSeekableInputsAreSupportedAndNeverClosed(bool fits)
    {
        using var buffer = new MemoryStream();
        if (fits) FitsWriter.Write(buffer, FitsConverter.FromXisf([TestImages.Developed()], ConversionLossPolicy.Allow));
        else XisfWriter.Write(buffer, [TestImages.Developed()]);
        using var input = new NonSeekableStream(buffer.ToArray());
        var image = Assert.Single(fits ? ScientificImageLoader.LoadFits(input) : ScientificImageLoader.LoadXisf(input));
        Assert.Equal(ImageStretch.None, image.Stretch);
        Assert.True(input.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedInputsFailWithoutClosingTheStream(bool fits)
    {
        using var input = new MemoryStream("not an image"u8.ToArray());
        Assert.ThrowsAny<Exception>(() => fits ? ScientificImageLoader.LoadFits(input) : ScientificImageLoader.LoadXisf(input));
        Assert.True(input.CanRead);
    }

    [Fact]
    public void FitsWithoutImagesReportsAnError()
    {
        using var input = new MemoryStream();
        FitsWriter.Write(input, new FitsDocument([FitsHdu.CreatePrimary()]));
        input.Position = 0;
        var error = Assert.Throws<InvalidDataException>(() => ScientificImageLoader.LoadFits(input));
        Assert.Contains("no image", error.Message);
    }

    [Fact]
    public void NonColorFitsCubeIsNotSilentlyFlattened()
    {
        using var input = new MemoryStream();
        var image = FitsImage.FromPhysicalSamples(new float[20], [2, 2, 5]);
        FitsWriter.Write(input, new FitsDocument([FitsHdu.CreateImage(image, true)]));
        input.Position = 0;
        Assert.Throws<NotSupportedException>(() => ScientificImageLoader.LoadFits(input));
    }

    private static MemoryStream MakeFits(short[] samples, string[] extraCards)
    {
        string[] cards = ["SIMPLE  =                    T", "BITPIX  =                   16", "NAXIS   =                    2",
            $"NAXIS1  = {samples.Length,20}", "NAXIS2  =                    1", .. extraCards, "END"];
        byte[] header = Encoding.ASCII.GetBytes(string.Concat(cards.Select(card => card.PadRight(80))).PadRight(2880));
        var stream = new MemoryStream();
        stream.Write(header);
        byte[] data = new byte[2880];
        for (int i = 0; i < samples.Length; i++) BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(i * 2), samples[i]);
        stream.Write(data);
        stream.Position = 0;
        return stream;
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
