using System.Text.RegularExpressions;

namespace PdnAstroFiles.Imaging;

internal static class MetadataRules
{
    private static readonly Regex Wcs = new(
        @"^(WCSAXES|WCSNAME|RADESYS|RADECSYS|EQUINOX|EPOCH|LONPOLE|LATPOLE|CRPIX\d+|CRVAL\d+|CTYPE\d+|CUNIT\d+|CDELT\d+|CROTA\d+|CD\d+_\d+|PC\d+_\d+|PV\d+_\d+|PS\d+_\d+|CNAME\d+|CRDER\d+|CSYER\d+)[A-Z]?$|^(A|B|AP|BP)(_ORDER|_\d+_\d+|_DMAX)$|^(CPDIS\d|DP\d\.|D2IMDIS\d|D2IM\d\.|DET2IM\d|LTM\d+_\d+|LTV\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex PixelProperty = new(@"^(PCL|Image):(Mean|Median|MAD|Variance|StdDev|StandardDeviation|Minimum|Maximum|Histogram)(:|$)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> PixelKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DATAMIN", "DATAMAX", "BUNIT", "PEDESTAL", "BLKLEVEL", "SATURATE", "BSOFTEN", "BOFFSET"
    };
    private static readonly HashSet<string> BayerKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "BAYERPAT", "XBAYROFF", "YBAYROFF", "XBAYEROFF", "YBAYEROFF", "BAYOFFX", "BAYOFFY", "CFAPAT", "CFATYPE"
    };
    private static readonly HashSet<string> StructuralKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SIMPLE", "XTENSION", "BITPIX", "NAXIS", "PCOUNT", "GCOUNT", "EXTEND", "BSCALE", "BZERO", "BLANK", "CHECKSUM", "DATASUM", "END"
    };

    public static bool IsStructuralKeyword(string name) => StructuralKeywords.Contains(name) ||
        name.StartsWith("NAXIS", StringComparison.OrdinalIgnoreCase);

    public static bool RemoveKeyword(string name, bool keepAstrometry, bool removeBayer)
    {
        string key = name.StartsWith("HIERARCH ", StringComparison.OrdinalIgnoreCase) ? name[9..].Trim() : name;
        return PixelKeywords.Contains(key) || (removeBayer && BayerKeywords.Contains(key)) ||
            (!keepAstrometry && Wcs.IsMatch(key));
    }

    public static bool RemoveProperty(string id, bool keepAstrometry) =>
        PixelProperty.IsMatch(id) ||
        id.StartsWith("Statistics:", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("Image:Histogram", StringComparison.OrdinalIgnoreCase) ||
        id == "Image:Pedestal" ||
        id.StartsWith("FITS:", StringComparison.OrdinalIgnoreCase) &&
            (IsStructuralKeyword(id[5..]) || RemoveKeyword(id[5..], keepAstrometry, true)) ||
        id.StartsWith("Image:Statistics", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("PCL:ImageStatistics", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("PCL:Histogram", StringComparison.OrdinalIgnoreCase) ||
        (!keepAstrometry && (id.StartsWith("AstrometricSolution:", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("PCL:Astrometric", StringComparison.OrdinalIgnoreCase)));
}
