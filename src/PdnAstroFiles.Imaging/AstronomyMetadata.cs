using System.Text.Json;
using System.Xml.Linq;
using XISFLib;

namespace PdnAstroFiles.Imaging;

public enum AstronomyFormat { Xisf, Fits }

public sealed record ImageOrigin(string Id, int SourceIndex, int Width, int Height, string Orientation,
    bool Debayered, ImageStretch Stretch)
{
    public int DisplayWidth => Orientation.StartsWith("90", StringComparison.Ordinal) || Orientation.StartsWith("-90", StringComparison.Ordinal) ? Height : Width;
    public int DisplayHeight => Orientation.StartsWith("90", StringComparison.Ordinal) || Orientation.StartsWith("-90", StringComparison.Ordinal) ? Width : Height;
}

/// <summary>A self-contained metadata template. Original full-size pixels are never retained.</summary>
public sealed record AstronomyMetadata(int Version, AstronomyFormat Format, byte[] Template, ImageOrigin[] Images)
{
    public const string DocumentKey = "PdnAstroFiles.Metadata.v1";
    public const string LayerKey = "PdnAstroFiles.ImageId.v1";
    public const string ErrorKey = "PdnAstroFiles.MetadataError";
    public const string DisplayProperty = "PdnAstroFiles:DisplayReferred";
    public const string DisplayKeyword = "PDNDISP";
    private const int MaximumTemplateBytes = 32 * 1024 * 1024;

    public string Serialize() => JsonSerializer.Serialize(this);

    public static AstronomyMetadata CreateEmpty(AstronomyFormat format, int width, int height)
    {
        var image = XisfImageData.FromSamples(new byte[] { 0 }, [1, 1], 1);
        using var stream = new MemoryStream();
        if (format == AstronomyFormat.Fits) FitsWriter.Write(stream, FitsConverter.FromXisf([image]));
        else XisfWriter.Write(stream, [image]);
        return Create(format, stream, [new(Guid.NewGuid().ToString("N"), 0, width, height, "0", false, ImageStretch.None)]);
    }

    public static AstronomyMetadata Deserialize(string value)
    {
        var result = JsonSerializer.Deserialize<AstronomyMetadata>(value)
            ?? throw new InvalidDataException("The original astronomy metadata is missing.");
        if (result.Version != 1 || result.Template.Length > MaximumTemplateBytes || result.Images.Length == 0 ||
            result.Images.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != result.Images.Length ||
            result.Images.Any(i => i.Width <= 0 || i.Height <= 0 || i.SourceIndex < 0))
            throw new InvalidDataException("The original astronomy metadata is invalid or uses an unsupported version.");
        return result;
    }

    internal static AstronomyMetadata Capture(XisfDocument document, IReadOnlyList<ScientificImageDisplay> displays)
    {
        var origins = document.Images.Select((image, index) => new ImageOrigin(Guid.NewGuid().ToString("N"), index,
            image.Dimensions[0], image.Dimensions[1], image.Orientation ?? "0", displays[index].BayerPattern is not null,
            displays[index].Stretch)).ToArray();
        var template = document.EditXml(xml =>
        {
            XNamespace ns = xml.Root!.Name.Namespace;
            foreach (var image in xml.Root.Elements(ns + "Image"))
            {
                foreach (string name in new[] { "compression", "checksum", "byteOrder", "bounds", "pixelStorage" })
                    image.Attribute(name)?.Remove();
                image.SetAttributeValue("geometry", "1:1:1");
                image.SetAttributeValue("sampleFormat", "UInt8");
                image.SetAttributeValue("colorSpace", "Gray");
                image.SetAttributeValue("location", "embedded");
                image.Elements(ns + "Data").Remove();
                image.Add(new XElement(ns + "Data", new XAttribute("encoding", "base64"), "AA=="));
                image.Elements(ns + "Thumbnail").Remove();
            }
        });
        using var stream = new MemoryStream();
        XisfWriter.Write(stream, template);
        return Create(AstronomyFormat.Xisf, stream, origins);
    }

    internal static AstronomyMetadata Capture(FitsDocument document, IReadOnlyList<ScientificImageDisplay> displays)
    {
        var origins = new List<ImageOrigin>();
        for (int index = 0; index < document.Hdus.Count; index++)
        {
            var hdu = document.Hdus[index];
            if (hdu.Image is not { } image) continue;
            var display = displays[origins.Count];
            origins.Add(new(Guid.NewGuid().ToString("N"), index, image.Dimensions[0], image.Dimensions[1], "0",
                display.BayerPattern is not null, display.Stretch));
            hdu.ReplaceImage(FitsImage.FromPhysicalSamples(new byte[] { 0 }, [1, 1]));
        }
        using var stream = new MemoryStream();
        FitsWriter.Write(stream, document);
        return Create(AstronomyFormat.Fits, stream, origins.ToArray());
    }

    private static AstronomyMetadata Create(AstronomyFormat format, MemoryStream stream, ImageOrigin[] origins)
    {
        if (stream.Length > MaximumTemplateBytes)
            throw new NotSupportedException("The file's non-image metadata exceeds the 32 MiB preservation limit.");
        return new(1, format, stream.ToArray(), origins);
    }
}
