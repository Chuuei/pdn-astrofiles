using XISFLib;

namespace PdnAstroFiles.Imaging;

/// <summary>Reads astronomy images without changing or closing the caller's input.</summary>
public static class ScientificImageLoader
{
    public static IReadOnlyList<ScientificImageDisplay> LoadXisf(Stream input)
    {
        var document = XisfReader.Load(input);
        var images = Prepare(document.Images.Select(XisfImageData.FromImage));
        try { return new ScientificImageSet(images, AstronomyMetadata.Capture(document, images)); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or XisfFormatException)
        {
            return new ScientificImageSet(images, null, ex.Message);
        }
    }

    public static IReadOnlyList<ScientificImageDisplay> LoadFits(Stream input)
    {
        var document = FitsReader.Load(input);
        var images = FitsConverter.ToXisf(document, new FitsConversionOptions
        {
            AxisInterpretation = FitsAxisInterpretation.RgbOrRgba,
            LossPolicy = ConversionLossPolicy.Allow,
            IncludeStructuralKeywords = true
        });
        var displays = Prepare(images);
        try { return new ScientificImageSet(displays, AstronomyMetadata.Capture(document, displays)); }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FitsFormatException)
        {
            return new ScientificImageSet(displays, null, ex.Message);
        }
    }

    private static IReadOnlyList<ScientificImageDisplay> Prepare(IEnumerable<XisfImageData> images)
    {
        var result = images.Select((image, index) => new ScientificImageDisplay(image, $"Image {index + 1}")).ToArray();
        if (result.Length == 0)
            throw new InvalidDataException("The file contains no image data.");
        return result;
    }
}
