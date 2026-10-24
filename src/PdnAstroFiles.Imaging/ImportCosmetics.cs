using System.Buffers;

namespace PdnAstroFiles.Imaging;

internal delegate void RgbaRowReader(int row, Span<float> destination);

/// <summary>Full-resolution color cosmetics using a bounded seven-row neighborhood.</summary>
internal sealed class ImportCosmetics(int width, int height, int colors, double noise, RgbaRowReader readRow)
{
    // Float equivalents of the reference finishing settings: selective Gaussian
    // radius 2 / sigma .75, thresholded radius 1 / sigma .7 unsharp at 20%,
    // and HSL saturation at 106%. Alpha is a coverage weight, never a filter target.
    private static readonly double[] SmoothingKernel = Kernel(2, 0.75);
    private static readonly double[] SharpeningKernel = Kernel(1, 0.7);
    private readonly double smoothingThreshold = Math.Clamp(2.5 * noise, 0.03, 0.15);
    private readonly double sharpeningThreshold = Math.Max(0.02, 2 * noise);

    public void CopyRow(int y, Span<float> destination)
    {
        int stride = checked(width * 4);
        float[] storage = ArrayPool<float>.Shared.Rent(checked(stride * 10));
        try
        {
            var raw = storage.AsSpan(0, stride * 7);
            var smooth = storage.AsSpan(stride * 7, stride * 3);
            for (int row = 0; row < 7; row++) readRow(Math.Clamp(y + row - 3, 0, height - 1), raw.Slice(row * stride, stride));
            // Compute each filtered neighbor from the original halo, avoiding tile seams
            // or dependencies on which rows the host requests first or in parallel.
            for (int row = 0; row < 3; row++)
            for (int x = 0; x < width; x++)
            {
                int center = (row + 2) * stride + x * 4;
                for (int c = 0; c < 3; c++)
                {
                    double value = raw[center + c], sum = 0, weight = 0;
                    if (noise > 1e-6 && raw[center + 3] > 0 && double.IsFinite(value))
                        for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                        {
                            int neighbor = (row + 2 + dy) * stride + Math.Clamp(x + dx, 0, width - 1) * 4;
                            double other = raw[neighbor + c];
                            if (!double.IsFinite(other) || Math.Abs(other - value) > smoothingThreshold) continue;
                            double w = SmoothingKernel[(dy + 2) * 5 + dx + 2] * raw[neighbor + 3];
                            sum += w * other;
                            weight += w;
                        }
                    smooth[row * stride + x * 4 + c] = weight > 0 ? (float)(sum / weight) : (float)value;
                }
                smooth[row * stride + x * 4 + 3] = raw[center + 3];
            }
            for (int x = 0; x < width; x++)
            {
                int target = x * 4, original = 3 * stride + target;
                for (int c = 0; c < 3; c++)
                {
                    double value = smooth[stride + target + c], sum = 0, weight = 0;
                    if (raw[original + 3] > 0 && double.IsFinite(value))
                        for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int neighbor = (dy + 1) * stride + Math.Clamp(x + dx, 0, width - 1) * 4;
                            double other = smooth[neighbor + c];
                            if (!double.IsFinite(other)) continue;
                            double w = SharpeningKernel[(dy + 1) * 3 + dx + 1] * smooth[neighbor + 3];
                            sum += w * other;
                            weight += w;
                        }
                    double detail = weight > 0 ? value - sum / weight : 0;
                    if (Math.Abs(detail) > sharpeningThreshold) value += 0.2 * detail;
                    destination[target + c] = Unit(value);
                }
                destination[target + 3] = raw[original + 3];
                if (colors == 3 && destination[target + 3] > 0 &&
                    float.IsFinite(raw[original]) && float.IsFinite(raw[original + 1]) && float.IsFinite(raw[original + 2]))
                    EnhanceSaturation(destination.Slice(target, 3));
            }
        }
        finally { ArrayPool<float>.Shared.Return(storage); }
    }

    private static void EnhanceSaturation(Span<float> rgb)
    {
        double min = Math.Min(rgb[0], Math.Min(rgb[1], rgb[2]));
        double max = Math.Max(rgb[0], Math.Max(rgb[1], rgb[2]));
        double chroma = max - min;
        if (chroma <= 0) return;
        double lightness = (min + max) * 0.5;
        double scale = Math.Min(1.06, (1 - Math.Abs(2 * lightness - 1)) / chroma);
        for (int c = 0; c < 3; c++) rgb[c] = Unit(lightness + (rgb[c] - lightness) * scale);
    }

    private static float Unit(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0;
    private static double[] Kernel(int radius, double sigma) =>
        (from y in Enumerable.Range(-radius, 2 * radius + 1)
         from x in Enumerable.Range(-radius, 2 * radius + 1)
         select Math.Exp(-(x * x + y * y) / (2 * sigma * sigma))).ToArray();
}
