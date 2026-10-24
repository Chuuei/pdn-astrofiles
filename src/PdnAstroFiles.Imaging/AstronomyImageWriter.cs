using System.Buffers.Binary;
using System.Globalization;
using System.Xml.Linq;
using XISFLib;

namespace PdnAstroFiles.Imaging;

/// <summary>Edited display-encoded RGBA samples, paired with their original image identifier.</summary>
public sealed record EditedImage(string OriginId, int Width, int Height, float[] Rgba,
    byte[]? IccProfile = null, double HorizontalDpi = 72, double VerticalDpi = 72);

public static class AstronomyImageWriter
{
    public static bool GeometryMatches(AstronomyMetadata metadata, int width, int height) =>
        width == metadata.Images.Max(origin => origin.DisplayWidth) && height == metadata.Images.Max(origin => origin.DisplayHeight);

    public static void Save(Stream output, AstronomyFormat format, AstronomyMetadata metadata,
        IReadOnlyList<EditedImage> images, bool geometryUnchanged)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (metadata.Version != 1 || !Enum.IsDefined(format))
            throw new NotSupportedException("The astronomy metadata version or requested format is not supported.");
        if (images.Count != metadata.Images.Length || images.Select(i => i.OriginId).Distinct(StringComparer.Ordinal).Count() != images.Count)
            throw new NotSupportedException("Every original astronomy image must have exactly one corresponding output image.");
        var prepared = metadata.Images.Select(origin =>
        {
            var image = images.SingleOrDefault(i => i.OriginId == origin.Id)
                ?? throw new NotSupportedException("An original astronomy image is missing or its metadata was removed.");
            // FITS cannot encode XISF display orientation. Bake it into the output,
            // removing coordinate metadata rather than retaining an incorrect solution.
            bool keepGeometry = geometryUnchanged && !(format == AstronomyFormat.Fits && metadata.Format == AstronomyFormat.Xisf && origin.Orientation != "0");
            return Prepare(image, origin, keepGeometry,
                format == AstronomyFormat.Fits || metadata.Format == AstronomyFormat.Fits ? AstronomyFormat.Fits : AstronomyFormat.Xisf);
        }).ToArray();
        if (format != metadata.Format)
        {
            SaveConverted(output, format, metadata, prepared);
            return;
        }
        if (format == AstronomyFormat.Xisf) SaveXisf(output, metadata, prepared);
        else SaveFits(output, metadata, prepared);
    }

    private static void SaveConverted(Stream output, AstronomyFormat format, AstronomyMetadata metadata, Prepared[] images)
    {
        // Run the ordinary cleanup first, then use the same explicit-loss conversion
        // policy as XISFLib's Avalonia viewer. Same-format preservation stays unchanged.
        using var native = new MemoryStream();
        if (metadata.Format == AstronomyFormat.Xisf) SaveXisf(native, metadata, images);
        else SaveFits(native, metadata, images);
        native.Position = 0;
        if (format == AstronomyFormat.Fits)
        {
            var source = XisfReader.Load(native);
            var converted = FitsConverter.ConvertToFits(source, ConversionLossPolicy.Allow);
            for (int index = 0; index < converted.Value.Hdus.Count; index++)
            {
                var header = converted.Value.Hdus[index].Header;
                AstronomyMetadataConversion.ToFits(header, source.Images[index].Properties, source.Properties);
                header.Set(AstronomyMetadata.DisplayKeyword, true, "Edited display-referred samples; do not auto-stretch");
                foreach (var finding in converted.Findings.Where(f => f.Effect == ConversionEffect.Omitted))
                    header.Add(new FitsHeaderCard("HISTORY", "AstroFiles conversion: " + finding.Message));
            }
            FitsWriter.Write(output, converted.Value, new() { WriteChecksums = true });
        }
        else
        {
            var converted = FitsConverter.ConvertToXisf(FitsReader.Load(native), new()
            {
                AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
                LossPolicy = ConversionLossPolicy.Allow
            });
            for (int index = 0; index < converted.Value.Count; index++)
            {
                var image = converted.Value[index];
                var edited = images[index].Edited;
                AstronomyMetadataConversion.ToXisf(image);
                image.Properties.Set(AstronomyMetadata.DisplayProperty, true);
                image.Resolution = new(edited.HorizontalDpi, edited.VerticalDpi, "inch");
                if (edited.IccProfile is { Length: > 0 } profile &&
                    (image.ChannelCount >= 3 || profile.Length >= 20 && profile.AsSpan(16, 4).SequenceEqual("GRAY"u8)))
                    image.IccProfile = profile;
                else if (image.ChannelCount >= 3) image.RgbWorkingSpace = XisfRgbWorkingSpace.Srgb;
                foreach (var finding in converted.Findings.Where(f => f.Effect == ConversionEffect.Omitted))
                    image.FitsKeywords.Add(XisfFitsKeyword.FromValue("HISTORY", "AstroFiles conversion: " + finding.Message));
            }
            XisfWriter.Write(output, converted.Value);
        }
    }

    private sealed record Prepared(EditedImage Edited, ImageOrigin Origin, int Width, int Height,
        int Channels, byte[] Pixels, bool KeepAstrometry, string Orientation)
    {
        public string History => $"AstroFiles: edited display-referred Float32 samples; " +
            $"import stretch={Origin.Stretch}; debayered={Origin.Debayered}; cosmetics={Origin.CosmeticsApplied}; " +
            (KeepAstrometry ? "geometry confirmed unchanged; astrometry retained." : "astrometry removed; geometry not confirmed or dimensions changed.");
    }

    private static Prepared Prepare(EditedImage image, ImageOrigin origin, bool geometryUnchanged, AstronomyFormat format)
    {
        if (image.Width <= 0 || image.Height <= 0 || image.Rgba.Length != checked(image.Width * image.Height * 4))
            throw new ArgumentException("The edited image buffer does not match its dimensions.");
        bool gray = true, alpha = false;
        for (int p = 0; p < image.Rgba.Length; p += 4)
        {
            gray &= image.Rgba[p] == image.Rgba[p + 1] && image.Rgba[p] == image.Rgba[p + 2];
            alpha |= image.Rgba[p + 3] != 1;
        }
        // FITS third axes of length two are scientific dimensions, not grayscale-alpha.
        if (format == AstronomyFormat.Fits && alpha) gray = false;
        int colors = gray ? 1 : 3, channels = colors + (alpha ? 1 : 0);
        bool keep = geometryUnchanged && image.Width == origin.DisplayWidth && image.Height == origin.DisplayHeight;
        int width = keep ? origin.Width : image.Width, height = keep ? origin.Height : image.Height;
        string orientation = keep ? origin.Orientation : "0";
        string rotation = orientation.Split(';')[0];
        bool flip = orientation == "flip" || orientation.EndsWith(";flip", StringComparison.Ordinal);
        int count = checked(width * height);
        byte[] pixels = new byte[checked(count * channels * sizeof(float))];
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            int tx = flip ? image.Width - 1 - x : x;
            (int sx, int sy) = rotation switch
            {
                "90" => (width - 1 - y, tx),
                "-90" => (y, height - 1 - tx),
                "180" => (width - 1 - tx, height - 1 - y),
                _ => (tx, y)
            };
            int target = sy * width + sx, source = (y * image.Width + x) * 4;
            for (int c = 0; c < channels; c++)
            {
                float value = image.Rgba[source + (c == colors ? 3 : c)];
                BinaryPrimitives.WriteSingleLittleEndian(pixels.AsSpan((c * count + target) * sizeof(float), sizeof(float)), value);
            }
        }
        return new(image, origin, width, height, channels, pixels, keep, orientation);
    }

    private static void SaveFits(Stream output, AstronomyMetadata metadata, Prepared[] images)
    {
        using var template = new MemoryStream(metadata.Template, writable: false);
        var document = FitsReader.Load(template);
        foreach (var image in images)
        {
            var hdu = document.Hdus[image.Origin.SourceIndex];
            int[] dimensions = image.Channels == 1 ? [image.Width, image.Height] : [image.Width, image.Height, image.Channels];
            byte[] big = (byte[])image.Pixels.Clone();
            for (int p = 0; p < big.Length; p += 4) big.AsSpan(p, 4).Reverse();
            hdu.ReplaceImage(FitsImage.FromRaw(big, -32, dimensions));
            for (int i = hdu.Header.Count - 1; i >= 0; i--)
                if (MetadataRules.RemoveKeyword(hdu.Header[i].Keyword, image.KeepAstrometry, image.Origin.Debayered || image.Channels >= 3))
                    hdu.Header.RemoveAt(i);
            hdu.Header.Set(AstronomyMetadata.DisplayKeyword, true, "Edited display-referred samples; do not auto-stretch");
            hdu.Header.Add(new FitsHeaderCard("HISTORY", image.History));
            hdu.Header.Add(new FitsHeaderCard("HISTORY", "AstroFiles: samples normalized; original photometric units, pedestal and pixel statistics removed."));
        }
        // An empty primary HDU may also contain inherited WCS and pixel metadata.
        if (document.Hdus[0].Image is null)
        {
            var header = document.Hdus[0].Header;
            for (int i = header.Count - 1; i >= 0; i--)
                if (MetadataRules.RemoveKeyword(header[i].Keyword, images.All(i => i.KeepAstrometry), images.Any(i => i.Origin.Debayered || i.Channels >= 3)))
                    header.RemoveAt(i);
        }
        FitsWriter.Write(output, document, new() { WriteChecksums = true });
    }

    private static void SaveXisf(Stream output, AstronomyMetadata metadata, Prepared[] images)
    {
        using var staged = new MemoryStream();
        staged.Write(metadata.Template);
        long[] locations = new long[images.Length];
        long[] profiles = new long[images.Length];
        for (int i = 0; i < images.Length; i++)
        {
            locations[i] = staged.Position;
            staged.Write(images[i].Pixels);
            profiles[i] = staged.Position;
            if (images[i].Edited.IccProfile is { Length: > 0 } profile) staged.Write(profile);
        }
        staged.Position = 0;
        var document = XisfReader.Load(staged).EditXml(xml =>
        {
            XNamespace ns = xml.Root!.Name.Namespace;
            var elements = xml.Root.Elements(ns + "Image").ToArray();
            for (int i = 0; i < images.Length; i++)
            {
                var image = images[i];
                var element = elements[image.Origin.SourceIndex];
                element.SetAttributeValue("geometry", $"{image.Width}:{image.Height}:{image.Channels}");
                element.SetAttributeValue("sampleFormat", "Float32");
                element.SetAttributeValue("pixelStorage", "Planar");
                element.SetAttributeValue("colorSpace", image.Channels < 3 ? "Gray" : "RGB");
                element.SetAttributeValue("bounds", "0:1");
                element.SetAttributeValue("byteOrder", "little");
                element.SetAttributeValue("location", $"attachment:{locations[i]}:{image.Pixels.Length}");
                element.SetAttributeValue("orientation", image.Orientation);
                element.SetAttributeValue("offset", "0");
                element.Attribute("checksum")?.Remove();
                element.Attribute("compression")?.Remove();
                element.Elements(ns + "Data").Remove();
                element.Elements(ns + "Thumbnail").Remove();
                element.Elements(ns + "DisplayFunction").Remove();
                element.Elements(ns + "ICCProfile").Remove();
                element.Elements(ns + "RGBWorkingSpace").Remove();
                if (image.Origin.Debayered || image.Channels >= 3) element.Elements(ns + "ColorFilterArray").Remove();
                foreach (var property in element.Elements(ns + "Property").ToArray())
                    if (MetadataRules.RemoveProperty((string?)property.Attribute("id") ?? "", image.KeepAstrometry) ||
                        (string?)property.Attribute("id") == AstronomyMetadata.DisplayProperty) property.Remove();
                foreach (var card in element.Elements(ns + "FITSKeyword").ToArray())
                {
                    string name = (string?)card.Attribute("name") ?? "";
                    if (MetadataRules.IsStructuralKeyword(name) || MetadataRules.RemoveKeyword(name, image.KeepAstrometry, image.Origin.Debayered || image.Channels >= 3)) card.Remove();
                }
                element.Add(new XElement(ns + "Property", new XAttribute("id", AstronomyMetadata.DisplayProperty),
                    new XAttribute("type", "Boolean"), new XAttribute("value", "true")));
                element.Add(new XElement(ns + "FITSKeyword", new XAttribute("name", "HISTORY"), new XAttribute("value", ""),
                    new XAttribute("comment", image.History)));
                element.Add(new XElement(ns + "FITSKeyword", new XAttribute("name", "HISTORY"), new XAttribute("value", ""),
                    new XAttribute("comment", "AstroFiles: normalized samples; old photometric units, pedestal and pixel statistics removed.")));
                if (image.Edited.IccProfile is { Length: > 0 } profile &&
                    (image.Channels >= 3 || profile.Length >= 20 && profile.AsSpan(16, 4).SequenceEqual("GRAY"u8)))
                    element.Add(new XElement(ns + "ICCProfile", new XAttribute("location", $"attachment:{profiles[i]}:{profile.Length}")));
                else if (image.Channels >= 3)
                {
                    var rgb = XisfRgbWorkingSpace.Srgb;
                    static string Join(IEnumerable<double> values) => string.Join(':', values.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                    element.Add(new XElement(ns + "RGBWorkingSpace", new XAttribute("gamma", "sRGB"),
                        new XAttribute("x", Join(rgb.ChromaticityCoordinates)), new XAttribute("y", Join(rgb.YChromaticityCoordinates)),
                        new XAttribute("Y", Join(rgb.LuminanceCoefficients)), new XAttribute("name", rgb.Name!)));
                }
                element.Elements(ns + "Resolution").Remove();
                bool quarterTurn = image.Orientation.StartsWith("90", StringComparison.Ordinal) || image.Orientation.StartsWith("-90", StringComparison.Ordinal);
                element.Add(new XElement(ns + "Resolution", new XAttribute("horizontal", (quarterTurn ? image.Edited.VerticalDpi : image.Edited.HorizontalDpi).ToString("R", CultureInfo.InvariantCulture)),
                    new XAttribute("vertical", (quarterTurn ? image.Edited.HorizontalDpi : image.Edited.VerticalDpi).ToString("R", CultureInfo.InvariantCulture)), new XAttribute("unit", "inch")));
            }
            foreach (var property in xml.Root.Elements(ns + "Property").ToArray())
                if (MetadataRules.RemoveProperty((string?)property.Attribute("id") ?? "", images.All(i => i.KeepAstrometry))) property.Remove();
        });
        XisfWriter.Write(output, document);
    }
}
