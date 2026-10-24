using System.IO;
using PaintDotNet.FileTypes;
using PaintDotNet.Imaging;
using PdnAstroFiles.Imaging;

namespace PdnAstroFiles;

internal static class AstronomyDocumentSaver
{
    public static AstronomyMetadata GetMetadata(IReadOnlyFileTypeDocument document)
    {
        if (!document.Metadata.Custom.TryGetValue(AstronomyMetadata.DocumentKey, out string? value))
        {
            document.Metadata.Custom.TryGetValue(AstronomyMetadata.ErrorKey, out string? reason);
            throw new NotSupportedException("The original astronomy metadata is unavailable. Reopen the original FITS/XISF file before editing and saving. " + reason);
        }
        return AstronomyMetadata.Deserialize(value);
    }

    public static void Save(IReadOnlyFileTypeDocument document, IImagingFactory imaging, Stream output,
        AstronomyFormat format, bool geometryUnchanged)
    {
        AstronomyMetadata original;
        try { original = GetMetadata(document); }
        catch (Exception ex) when (ex is NotSupportedException or InvalidDataException or System.Text.Json.JsonException)
        {
            original = AstronomyMetadata.CreateEmpty(format, document.Size.Width, document.Size.Height);
            geometryUnchanged = false;
        }
        using var color = document.GetColorContext();
        byte[]? profile = color.GetProfileBytes().IsEmpty ? null : color.GetProfileBytes().ToArray();
        double units = document.Resolution.Units == PaintDotNet.MeasurementUnit.Centimeter ? 2.54 : 1;
        double dpiX = document.Resolution.X * units, dpiY = document.Resolution.Y * units;
        var images = new List<EditedImage>();
        if (original.Images.Length == 1)
        {
            using var bitmap = document.GetCompositeBitmap(PixelFormats.Rgba128Float);
            images.Add(new(original.Images[0].Id, bitmap.Size.Width, bitmap.Size.Height, ReadPixels(bitmap), profile, dpiX, dpiY));
        }
        else
        {
            if (document.Layers.Count != original.Images.Length)
                throw new NotSupportedException("For a file with multiple astronomy images, retain one original layer per image. Added, deleted or merged layers cannot be saved without losing their metadata; use PDN instead.");
            foreach (var layer in document.Layers)
            {
                if (!layer.Metadata.Custom.TryGetValue(AstronomyMetadata.LayerKey, out string? id) ||
                    !original.Images.Any(i => i.Id == id))
                    throw new NotSupportedException("A layer no longer has its original astronomy metadata; use PDN to retain the edited document.");
                if (layer.Opacity != 1 || layer.BlendMode != PaintDotNet.LayerBlendMode.Normal)
                    throw new NotSupportedException("Multiple astronomy images are saved individually. Layer opacity and blending cannot be represented; use PDN to retain these settings.");
                using var bitmap = layer.GetBitmap();
                using var converted = imaging.CreateFormatConvertedBitmap(bitmap, PixelFormats.Rgba128Float);
                int width = bitmap.Size.Width, height = bitmap.Size.Height;
                float[] pixels = ReadPixels(converted);
                var origin = original.Images.Single(i => i.Id == id);
                if (AstronomyImageWriter.GeometryMatches(original, width, height) &&
                    PaddingIsEmpty(pixels, width, height, origin.DisplayWidth, origin.DisplayHeight))
                {
                    var cropped = new float[checked(origin.DisplayWidth * origin.DisplayHeight * 4)];
                    for (int y = 0; y < origin.DisplayHeight; y++)
                        pixels.AsSpan(y * width * 4, origin.DisplayWidth * 4).CopyTo(cropped.AsSpan(y * origin.DisplayWidth * 4));
                    pixels = cropped;
                    width = origin.DisplayWidth; height = origin.DisplayHeight;
                }
                images.Add(new(id, width, height, pixels, profile, dpiX, dpiY));
            }
        }
        AstronomyImageWriter.Save(output, format, original, images, geometryUnchanged);
    }

    private static unsafe float[] ReadPixels(IBitmapSource bitmap)
    {
        var pixels = new float[checked(bitmap.Size.Width * bitmap.Size.Height * 4)];
        fixed (float* pointer = pixels)
            bitmap.CopyPixels(pointer, checked(bitmap.Size.Width * 16), checked((uint)pixels.Length * sizeof(float)), null);
        return pixels;
    }

    private static bool PaddingIsEmpty(float[] pixels, int width, int height, int originalWidth, int originalHeight)
    {
        for (int y = 0; y < height; y++)
        for (int x = y < originalHeight ? originalWidth : 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            if (pixels[p] != 0 || pixels[p + 1] != 0 || pixels[p + 2] != 0 || pixels[p + 3] != 0) return false;
        }
        return true;
    }
}
