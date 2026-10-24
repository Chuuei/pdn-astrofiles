using System.IO;
using System.Reflection;
using PaintDotNet.ComponentModel;
using PaintDotNet.FileTypes;
using PaintDotNet.Imaging;
using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Plugin.Tests;

public sealed class DocumentLifetimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BayerMosaicsReachTheHostBitmapAsRgb(bool fits)
    {
        float[] samples = Enumerable.Range(0, 35).Select(p => "RGGB"[((p / 7) & 1) * 2 + ((p % 7) & 1)] switch
        {
            'R' => 0.8f, 'G' => 0.4f, _ => 0.2f
        }).ToArray();
        var source = XisfImageData.FromSamples(samples, [7, 5], 1);
        if (fits) source.FitsKeywords.Add(XisfFitsKeyword.FromValue("BAYERPAT", "RGGB"));
        else source.ColorFilterArray = new("RGGB", 2, 2, "Bayer");
        using var stream = new MemoryStream();
        if (fits) FitsWriter.Write(stream, FitsConverter.FromXisf([source], ConversionLossPolicy.Allow));
        else XisfWriter.Write(stream, [source]);
        stream.Position = 0;

        var assembly = typeof(IFileTypeDocumentFactory).Assembly;
        using var factory = (IFileTypeDocumentFactory)Activator.CreateInstance(
            assembly.GetType("PaintDotNet.FileTypes.FileTypeDocumentFactory", throwOnError: true)!, nonPublic: true)!;
        var context = (IFileTypeLoadContext)Activator.CreateInstance(
            assembly.GetType("PaintDotNet.FileTypes.FileTypeLoadContext", throwOnError: true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [factory, stream], null)!;
        using var contextLifetime = (IDisposable)context;
        using var imaging = ImagingFactory.CreateRef();
        var images = fits ? ScientificImageLoader.LoadFits(stream) : ScientificImageLoader.LoadXisf(stream);
        using var document = AstronomyDocument.Create(context, imaging, images);
        using var layer = document.Layers[0].CreateRef<IFileTypeBitmapLayer<ColorRgba128Float>>();
        using var bitmap = layer.GetBitmap();
        using var bitmapLock = bitmap.Lock(BitmapLockOptions.Read);
        var pixels = bitmapLock.AsRegionPtr();
        for (int y = 0; y < 5; y++)
        foreach (var pixel in pixels.Rows[y].Span)
        {
            Assert.Equal(0.8f, pixel.R, 6);
            Assert.Equal(0.4f, pixel.G, 6);
            Assert.Equal(0.2f, pixel.B, 6);
            Assert.Equal(1f, pixel.A);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReturnedDocumentRetainsReadableLayersAfterImportHandlesAreDisposed(bool fits, bool multiple)
    {
        var first = XisfImageData.FromSamples(new[] { 0f, 0.25f, 0.75f, 1f }, [2, 2], 1);
        var second = XisfImageData.FromSamples(new[] { 0f, 0.3f, 0.6f, 1f }, [4, 1], 1);
        XisfImageData[] sources = multiple ? [first, second] : [first];
        using var stream = new MemoryStream();
        if (fits) FitsWriter.Write(stream, FitsConverter.FromXisf(sources, ConversionLossPolicy.Allow));
        else XisfWriter.Write(stream, sources);
        stream.Position = 0;

        // These concrete host implementation types are intentionally absent from the public
        // API. Construct the actual preview objects rather than mocking their ref counting.
        var assembly = typeof(IFileTypeDocumentFactory).Assembly;
        using var factory = (IFileTypeDocumentFactory)Activator.CreateInstance(
            assembly.GetType("PaintDotNet.FileTypes.FileTypeDocumentFactory", throwOnError: true)!, nonPublic: true)!;
        var context = (IFileTypeLoadContext)Activator.CreateInstance(
            assembly.GetType("PaintDotNet.FileTypes.FileTypeLoadContext", throwOnError: true)!,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, [factory, stream], null)!;
        using var contextLifetime = (IDisposable)context;
        using var imaging = ImagingFactory.CreateRef();

        var images = fits ? ScientificImageLoader.LoadFits(stream) : ScientificImageLoader.LoadXisf(stream);
        using var document = AstronomyDocument.Create(context, imaging, images);

        Assert.False(document.Layers.IsDisposed);
        using var layers = document.Layers.CreateRef();
        Assert.Equal(sources.Length, layers.Count);
        for (int index = 0; index < layers.Count; index++)
        {
            Assert.False(layers[index].IsDisposed);
            Assert.Equal(index == 0, layers[index].Visible);
            using var layer = layers[index].CreateRef<IFileTypeBitmapLayer<ColorRgba128Float>>();
            using var bitmap = layer.GetBitmap();
            using var bitmapLock = bitmap.Lock(BitmapLockOptions.Read);
            var pixels = bitmapLock.AsRegionPtr();
            Assert.Equal(index == 0 ? 0.25f : 0.3f, pixels.Rows[0].Span[1].R);
            Assert.Equal(1f, pixels.Rows[0].Span[1].A);
            if (multiple && index == 0) Assert.Equal(0f, pixels.Rows[0].Span[3].A);
            if (multiple && index == 1) Assert.Equal(0f, pixels.Rows[1].Span[0].A);
        }
    }
}
