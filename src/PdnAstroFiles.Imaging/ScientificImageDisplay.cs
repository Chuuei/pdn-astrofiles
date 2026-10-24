using System.Buffers.Binary;
using XISFLib;

namespace PdnAstroFiles.Imaging;

public enum ImageStretch
{
    None,
    Stored,
    Automatic
}

/// <summary>
/// Full-resolution, straight-alpha RGBA display of scientific samples. Output RGB values
/// are display encoded, not linear-light scRGB. The source samples remain unchanged.
/// </summary>
public sealed class ScientificImageDisplay
{
    private const double AutomaticBackground = 0.20;
    private readonly XisfImageData source;
    private readonly int colors;
    private readonly string rotation;
    private readonly bool flip;
    private readonly XisfDisplayFunction? function;
    private readonly double pedestal;
    private readonly Transform[] transforms;

    public ScientificImageDisplay(XisfImageData source, string defaultName = "Image")
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Dimensions.Count != 2)
            throw new NotSupportedException("Only two-dimensional images and FITS RGB/RGBA third axes can be opened.");
        if (source.ColorSpace is not (null or "Gray" or "RGB"))
            throw new NotSupportedException($"The {source.ColorSpace} color space is not supported. Use grayscale or RGB.");
        int sourceColors = source.ColorSpace == "RGB" ? 3 : 1;
        if (source.ChannelCount < sourceColors || source.ChannelCount > sourceColors + 1)
            throw new NotSupportedException("Only grayscale, grayscale-alpha, RGB and RGBA images can be opened.");
        if (source.SampleFormat is not (XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or
            XisfSampleFormat.UInt32 or XisfSampleFormat.UInt64 or XisfSampleFormat.Float32 or XisfSampleFormat.Float64))
            throw new NotSupportedException($"The {source.SampleFormat} sample format cannot be displayed.");

        this.source = source;
        BayerPattern = BayerMetadata.Detect(source);
        colors = BayerPattern is null ? sourceColors : 3;
        string orientation = source.Orientation ?? "0";
        if (orientation is not ("0" or "flip" or "0;flip" or "90" or "90;flip" or "-90" or "-90;flip" or "180" or "180;flip"))
            throw new NotSupportedException($"Unsupported XISF orientation: {orientation}.");
        rotation = orientation.Split(';')[0];
        flip = orientation.EndsWith(";flip", StringComparison.Ordinal) || orientation == "flip";
        bool quarterTurn = rotation is "90" or "-90";
        Width = source.Dimensions[quarterTurn ? 1 : 0];
        Height = source.Dimensions[quarterTurn ? 0 : 1];
        Name = string.IsNullOrWhiteSpace(source.Id) ? defaultName : source.Id;

        var resolution = source.Resolution;
        double units = resolution?.Unit.Equals("cm", StringComparison.OrdinalIgnoreCase) == true ? 2.54 : 1;
        double horizontal = (resolution?.Horizontal ?? 72) * units;
        double vertical = (resolution?.Vertical ?? 72) * units;
        HorizontalDpi = ValidDpi(quarterTurn ? vertical : horizontal);
        VerticalDpi = ValidDpi(quarterTurn ? horizontal : vertical);

        var reader = new SampleReader(source, BayerPattern);
        double[][] samples = Sample(reader);
        double[] medians = samples.Select(Median).ToArray();
        double median = medians.Average();
        double spread = samples.Select((values, c) => Median(values.Select(v => Math.Abs(v - medians[c])).ToArray()) * 1.4826).Average();

        // Same median/MAD decision as SkyBrowser's SkyImageFinisher.LooksLinear.
        // A narrow high background is an additive pedestal, not evidence of a developed image.
        bool highPedestal = median >= 0.18 && spread > 0 && spread < median * 0.1;
        bool savedDisplay = source.Properties.TryGetValue<bool>(AstronomyMetadata.DisplayProperty, out bool displayReferred) && displayReferred ||
            source.FitsKeywords.Any(card => card.Name.Equals(AstronomyMetadata.DisplayKeyword, StringComparison.OrdinalIgnoreCase) && card.Value.Trim() == "T");
        bool linear = !savedDisplay && double.IsFinite(median) && spread > 1e-10 && (median < 0.18 || highPedestal);
        if (linear && source.DisplayFunction is not null)
        {
            Stretch = ImageStretch.Stored;
            function = source.DisplayFunction;
        }
        else if (linear)
        {
            Stretch = ImageStretch.Automatic;
            pedestal = highPedestal ? Math.Max(0, median - AutomaticBackground) : 0;
            function = XisfDisplayFunctions.CreateAdaptive(
                samples.Select(values => values.Select(value => value - pedestal).ToArray()).ToArray(),
                targetBackground: AutomaticBackground, clipping: -2.8, linkChannels: true);
        }

        // A stored grayscale display function uses component zero. Reconstructed RGB
        // uses that same transfer for every color, preserving the source's color balance.
        transforms = Enumerable.Range(0, colors).Select(channel => BayerPattern is null ? channel : 0).Select(component => function is null
            ? new Transform(0.5, 0, 1, 0, 1)
            : new Transform(function.Midtones[component], function.Shadows[component], function.Highlights[component],
                function.LowRange[component], function.HighRange[component])).ToArray();
    }

    public int Width { get; }
    public int Height { get; }
    public string Name { get; }
    public double HorizontalDpi { get; }
    public double VerticalDpi { get; }
    public ImageStretch Stretch { get; }
    public string? BayerPattern { get; }
    // A mosaic's grayscale profile does not describe the newly reconstructed RGB data.
    public ReadOnlyMemory<byte> IccProfile => BayerPattern is null ? source.IccProfile ?? ReadOnlyMemory<byte>.Empty : ReadOnlyMemory<byte>.Empty;

    /// <summary>Copies one oriented row to interleaved RGBA floats, with no 8-bit quantization.</summary>
    public void CopyRgbaRow(int y, Span<float> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        if (destination.Length < checked(Width * 4))
            throw new ArgumentException("The destination is too short for an RGBA row.", nameof(destination));
        var reader = new SampleReader(source, BayerPattern);
        int sourceWidth = source.Dimensions[0], sourceHeight = source.Dimensions[1];
        for (int x = 0; x < Width; x++)
        {
            int tx = flip ? Width - 1 - x : x;
            (int sx, int sy) = rotation switch
            {
                "90" => (sourceWidth - 1 - y, tx),
                "-90" => (y, sourceHeight - 1 - tx),
                "180" => (sourceWidth - 1 - tx, sourceHeight - 1 - y),
                _ => (tx, y)
            };
            int pixel = checked(sy * sourceWidth + sx);
            int target = x * 4;
            for (int c = 0; c < 3; c++)
            {
                int channel = colors == 1 ? 0 : c;
                double value = reader.Read(pixel, channel);
                destination[target + c] = (float)Unit(function is null ? value : transforms[channel].Apply(value - pedestal));
            }
            destination[target + 3] = source.ChannelCount > colors ? (float)Unit(reader.Read(pixel, colors)) : 1;
        }
    }

    private double[][] Sample(SampleReader reader)
    {
        int count = checked(source.Dimensions[0] * source.Dimensions[1]);
        int step = Math.Max(1, (int)Math.Ceiling(count / 65536d));
        var channels = Enumerable.Range(0, colors).Select(_ => new List<double>()).ToArray();
        for (int pixel = 0; pixel < count; pixel += step)
        {
            if (source.ChannelCount > colors && Unit(reader.Read(pixel, colors)) <= 0)
                continue;
            for (int c = 0; c < colors; c++)
            {
                double value = reader.Read(pixel, c);
                if (double.IsFinite(value)) channels[c].Add(Math.Clamp(value, 0, 1));
            }
        }
        return channels.Select(channel => channel.ToArray()).ToArray();
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 0;
        // Do not sort the caller's samples: their order must remain intact for the MAD pass.
        double[] sorted = (double[])values.Clone();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) * 0.5 : sorted[middle];
    }

    private static double Unit(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
    private static double ValidDpi(double value) => double.IsFinite(value) && value > 0 ? value : 72;

    private readonly record struct Transform(double Midtones, double Shadows, double Highlights, double Low, double High)
    {
        public double Apply(double value) => double.IsFinite(value)
            ? XisfDisplayFunctions.Evaluate(value, Midtones, Shadows, Highlights, Low, High) : 0;
    }

    private readonly ref struct SampleReader
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly int pixels, channels, size, width, height;
        private readonly bool planar, big;
        private readonly XisfSampleFormat format;
        private readonly double lower, range;
        private readonly string? pattern;

        public SampleReader(XisfImageData image, string? pattern) : this(image)
        {
            this.pattern = pattern;
        }

        public SampleReader(XisfImageData image)
        {
            data = image.RawData;
            width = image.Dimensions[0];
            height = image.Dimensions[1];
            pixels = checked(width * height);
            channels = image.ChannelCount;
            planar = image.PixelStorage == XisfPixelLayout.Planar;
            big = image.IsBigEndian;
            format = image.SampleFormat;
            size = format switch
            {
                XisfSampleFormat.UInt8 => 1,
                XisfSampleFormat.UInt16 => 2,
                XisfSampleFormat.UInt32 or XisfSampleFormat.Float32 => 4,
                _ => 8
            };
            lower = image.LowerBound ?? 0;
            double upper = image.UpperBound ?? (format switch
            {
                XisfSampleFormat.UInt8 => byte.MaxValue,
                XisfSampleFormat.UInt16 => ushort.MaxValue,
                XisfSampleFormat.UInt32 => uint.MaxValue,
                XisfSampleFormat.UInt64 => ulong.MaxValue,
                _ => 1
            });
            range = upper - lower;
            if (!double.IsFinite(lower) || !double.IsFinite(range) || range <= 0)
                throw new NotSupportedException("Displaying the image requires finite increasing sample bounds.");
        }

        public double Read(int pixel, int channel)
        {
            if (pattern is null) return (Value(pixel, channel) - lower) / range;
            int x = pixel % width, y = pixel / width;
            char color = "RGB"[channel];
            if (pattern[(y & 1) * 2 + (x & 1)] == color)
                return (Value(pixel, 0) - lower) / range;

            // Bilinear reconstruction uses available matching neighbors at image edges.
            // Work directly from the original mosaic, avoiding an image-sized RGB copy.
            int x0 = Math.Max(0, x - 1), x1 = Math.Min(width - 1, x + 1);
            int y0 = Math.Max(0, y - 1), y1 = Math.Min(height - 1, y + 1);
            int count = 0;
            for (int yy = y0; yy <= y1; yy++)
            for (int xx = x0; xx <= x1; xx++)
                if (pattern[(yy & 1) * 2 + (xx & 1)] == color) count++;

            bool integer = format is XisfSampleFormat.UInt8 or XisfSampleFormat.UInt16 or XisfSampleFormat.UInt32 or XisfSampleFormat.UInt64;
            UInt128 sum = 0;
            double floating = 0;
            for (int yy = y0; yy <= y1; yy++)
            for (int xx = x0; xx <= x1; xx++)
            {
                if (pattern[(yy & 1) * 2 + (xx & 1)] != color) continue;
                int neighbor = yy * width + xx;
                if (integer) sum += Bits(neighbor, 0);
                else floating += Value(neighbor, 0) / count;
            }
            // Match SkyBrowser: integer interpolation rounds down with an exact wide sum;
            // Float32 interpolation retains the original sample precision.
            double average = integer ? (double)(ulong)(sum / (uint)count)
                : format == XisfSampleFormat.Float32 ? (float)floating : floating;
            return (average - lower) / range;
        }

        private ulong Bits(int pixel, int channel)
        {
            long index = planar ? (long)channel * pixels + pixel : (long)pixel * channels + channel;
            var bytes = data.Slice(checked((int)(index * size)), size);
            return size switch
            {
                1 => bytes[0],
                2 => big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                4 => big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                _ => big ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            };
        }

        private double Value(int pixel, int channel)
        {
            ulong bits = Bits(pixel, channel);
            return format switch
            {
                XisfSampleFormat.Float32 => BitConverter.Int32BitsToSingle(unchecked((int)bits)),
                XisfSampleFormat.Float64 => BitConverter.Int64BitsToDouble(unchecked((long)bits)),
                _ => bits
            };
        }
    }
}
