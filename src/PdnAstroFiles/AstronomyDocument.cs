using System.Runtime.InteropServices;
using PaintDotNet;
using PaintDotNet.FileTypes;
using PaintDotNet.Imaging;
using PdnAstroFiles.Imaging;

namespace PdnAstroFiles;

internal static class AstronomyDocument
{
    public static IFileTypeDocument Create(IFileTypeLoadContext context, IImagingFactory imaging,
        IReadOnlyList<ScientificImageDisplay> images)
    {
        var first = images[0];
        if (images.Any(image => !image.IccProfile.Span.SequenceEqual(first.IccProfile.Span)))
            throw new NotSupportedException("Images with different ICC profiles cannot be imported as layers of a single document.");

        int width = images.Max(image => image.Width), height = images.Max(image => image.Height);
        var document = context.Factory.CreateDocument<ColorRgba128Float>(width, height);
        try
        {
            // These are display-encoded samples. An explicit profile avoids interpreting
            // the stretched sky as linear scRGB and unintentionally brightening it on import.
            using (var color = first.IccProfile.IsEmpty
                ? imaging.CreateColorContext(ExifColorSpace.Srgb)
                : imaging.CreateColorContext(first.IccProfile.Span))
            {
                document.SetColorContext(color);
            }
            document.Resolution = new Resolution(first.HorizontalDpi, first.VerticalDpi, MeasurementUnit.Inch);
            var preserved = images as ScientificImageSet;
            using (var metadata = document.Metadata.Custom.CreateTransaction())
            {
                if (preserved?.Metadata is { } original) metadata[AstronomyMetadata.DocumentKey] = original.Serialize();
                else metadata[AstronomyMetadata.ErrorKey] = preserved?.MetadataError ?? "No original astronomy metadata was captured.";
            }

            // Layers is borrowed from the document. Own a separate reference so disposing
            // this local handle cannot dispose the document's list and clear its layers.
            using var layers = document.Layers.CreateRefT();
            for (int index = 0; index < images.Count; index++)
            {
                var image = images[index];
                using var layer = document.CreateBitmapLayer();
                layer.Name = image.Name;
                layer.Visible = index == 0;
                layer.Opacity = 1;
                if (preserved?.Metadata is { } originalMetadata)
                {
                    using var metadata = layer.Metadata.Custom.CreateTransaction();
                    metadata[AstronomyMetadata.LayerKey] = originalMetadata.Images[index].Id;
                }
                using (var bitmap = layer.GetBitmap())
                using (var bitmapLock = bitmap.Lock(BitmapLockOptions.Write))
                {
                    var region = bitmapLock.AsRegionPtr();
                    Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, 8) }, y =>
                    {
                        Span<float> row = MemoryMarshal.Cast<ColorRgba128Float, float>(region.Rows[y].Span);
                        row.Clear();
                        if (y < image.Height) image.CopyRgbaRow(y, row);
                    });
                }
                layers.Add(layer);
            }
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }
}
