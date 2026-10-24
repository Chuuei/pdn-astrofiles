using System.Globalization;
using XISFLib;

namespace PdnAstroFiles.Imaging;

/// <summary>Common acquisition fields whose meanings and units can be mapped without solving astrometry.</summary>
internal static class AstronomyMetadataConversion
{
    private enum Kind { Text, Float, Double, Integer, Time }
    private sealed record Mapping(string Property, string Keyword, Kind Kind, double FitsScale = 1);
    private static readonly Mapping[] Mappings =
    [
        new("Observation:Object:Name", "OBJECT", Kind.Text),
        new("Observation:Time:Start", "DATE-OBS", Kind.Time),
        new("Observation:Time:End", "DATE-END", Kind.Time),
        new("Instrument:ExposureTime", "EXPTIME", Kind.Float),
        new("Instrument:Telescope:Name", "TELESCOP", Kind.Text),
        new("Instrument:Camera:Name", "INSTRUME", Kind.Text),
        new("Instrument:Filter:Name", "FILTER", Kind.Text),
        new("Instrument:Telescope:FocalLength", "FOCALLEN", Kind.Float, 1000),
        new("Instrument:Sensor:Temperature", "CCD-TEMP", Kind.Float),
        new("Instrument:Sensor:TargetTemperature", "SET-TEMP", Kind.Float),
        new("Instrument:Sensor:XPixelSize", "XPIXSZ", Kind.Float),
        new("Instrument:Sensor:YPixelSize", "YPIXSZ", Kind.Float),
        new("Instrument:Camera:XBinning", "XBINNING", Kind.Integer),
        new("Instrument:Camera:YBinning", "YBINNING", Kind.Integer),
        new("Instrument:Camera:Gain", "EGAIN", Kind.Float),
        new("Observation:Object:RA", "RA", Kind.Double),
        new("Observation:Object:Dec", "DEC", Kind.Double)
    ];

    public static void ToFits(FitsHeader header, IReadOnlyDictionary<string, XisfProperty> image,
        IReadOnlyDictionary<string, XisfProperty> global)
    {
        foreach (var mapping in Mappings)
        {
            // Explicit source FITS cards take precedence over synthesized equivalents.
            if (header.FindFirst(mapping.Keyword) is not null) continue;
            if ((!image.TryGetValue(mapping.Property, out var property) && !global.TryGetValue(mapping.Property, out property)) ||
                !property.IsAvailable || property.Value is not { } value) continue;
            if (mapping.Kind == Kind.Text && value is string text) header.Set(mapping.Keyword, text);
            else if (mapping.Kind == Kind.Time && TryTime(Convert.ToString(value, CultureInfo.InvariantCulture), value, out var time))
                header.Set(mapping.Keyword, time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture));
            else if (mapping.Kind is Kind.Float or Kind.Double or Kind.Integer &&
                double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double number) &&
                double.IsFinite(number * mapping.FitsScale))
                header.Set(mapping.Keyword, number * mapping.FitsScale);
        }
    }

    public static void ToXisf(XisfImageData image)
    {
        foreach (var mapping in Mappings)
        {
            if (image.Properties.ContainsKey(mapping.Property)) continue;
            var card = image.FitsKeywords.FirstOrDefault(k => k.Name.Equals(mapping.Keyword, StringComparison.OrdinalIgnoreCase));
            if (card is null) continue;
            string text = card.Value.Trim();
            bool quoted = text.Length >= 2 && text[0] == '\'' && text[^1] == '\'';
            if (quoted) text = text[1..^1].Replace("''", "'", StringComparison.Ordinal);
            if (mapping.Kind == Kind.Text && quoted) image.Properties[mapping.Property] = text;
            else if (mapping.Kind == Kind.Time && TryTime(text, null, out var time)) image.Properties.Set(mapping.Property, time);
            else if (mapping.Kind is Kind.Float or Kind.Double or Kind.Integer && !quoted &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                number /= mapping.FitsScale;
                if (!double.IsFinite(number)) continue;
                if (mapping.Kind == Kind.Float && double.IsFinite(number) && Math.Abs(number) <= float.MaxValue)
                    image.Properties.Set(mapping.Property, (float)number);
                else if (mapping.Kind == Kind.Double) image.Properties.Set(mapping.Property, number);
                else if (mapping.Kind == Kind.Integer && number == Math.Truncate(number) && number >= int.MinValue && number <= int.MaxValue)
                    image.Properties.Set(mapping.Property, (int)number);
            }
        }
    }

    private static bool TryTime(string? text, object? value, out DateTimeOffset time)
    {
        if (value is DateTimeOffset existing) { time = existing; return true; }
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
    }
}
