using System.Globalization;
using XISFLib;

namespace PdnAstroFiles.Imaging;

/// <summary>Resolves a recorded Bayer tile in stored pixel coordinates, following SkyBrowser.</summary>
internal static class BayerMetadata
{
    public static string? Detect(XisfImageData image)
    {
        // RGB/RGBA and grayscale-alpha data are already multi-channel, even if stale
        // acquisition metadata still describes the original camera sensor.
        if (image.ChannelCount != 1 || image.Dimensions.Count != 2 ||
            image.Dimensions[0] < 2 || image.Dimensions[1] < 2 ||
            (image.ColorSpace is not null && !image.ColorSpace.Equals("Gray", StringComparison.OrdinalIgnoreCase)) ||
            image.SampleFormat is not (XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or XisfSampleFormat.UInt32 or
                XisfSampleFormat.UInt64 or XisfSampleFormat.Float32 or XisfSampleFormat.Float64))
            return null;

        static bool Supported(string pattern) => pattern is "RGGB" or "BGGR" or "GRBG" or "GBRG";
        if (image.ColorFilterArray is { } cfa)
        {
            string native = cfa.Pattern.Trim().ToUpperInvariant();
            return cfa.Width == 2 && cfa.Height == 2 && Supported(native) ? native : null;
        }

        string? Text(string name) => image.FitsKeywords.FirstOrDefault(card =>
            card.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            card.Name.Equals($"HIERARCH {name}", StringComparison.OrdinalIgnoreCase))?.Value.Trim().Trim('\'').Trim();
        string? pattern = Text("BAYERPAT")?.ToUpperInvariant();
        if (pattern is null || !Supported(pattern)) return null;

        bool Offset(string shortName, string longName, out long value)
        {
            string? text = Text(shortName) ?? Text(longName);
            value = 0;
            return text is null || long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        if (!Offset("XBAYROFF", "XBAYEROFF", out long x) || !Offset("YBAYROFF", "YBAYEROFF", out long y))
            return null;
        string? rowOrder = Text("ROWORDER")?.ToUpperInvariant();
        if (rowOrder is not (null or "TOP-DOWN" or "BOTTOM-UP")) return null;

        // Native XISF CFA already describes stored rows. FITS BOTTOM-UP additionally
        // shifts the sensor tile's row parity; it does not flip the actual image data.
        int rowParity = (int)(y & 1) ^ (rowOrder == "BOTTOM-UP" ? (image.Dimensions[1] - 1) & 1 : 0);
        return string.Concat(Enumerable.Range(0, 4).Select(i =>
            pattern[(((i / 2) + rowParity) & 1) * 2 + (((i % 2) + (int)(x & 1)) & 1)]));
    }
}
