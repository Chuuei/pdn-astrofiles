using PdnAstroFiles.Imaging;
using XISFLib;

namespace PdnAstroFiles.Imaging.Tests;

internal static class TestImages
{
    public static XisfImageData Linear(double pedestal = 0, bool alpha = false)
    {
        const int width = 64, height = 48, count = width * height;
        float[] samples = new float[count * (alpha ? 4 : 3)];
        for (int p = 0; p < count; p++)
        {
            for (int c = 0; c < 3; c++)
                samples[c * count + p] = (float)(pedestal + 0.003 + c * 0.0007 + 0.0006 * ((p * 37) % 101) / 100);
            if (alpha) samples[3 * count + p] = p % 3 == 0 ? 0 : p % 3 == 1 ? 0.5f : 1;
        }
        return XisfImageData.FromSamples(samples, [width, height], alpha ? 4 : 3);
    }

    public static XisfImageData Developed()
    {
        float[] values = [0.02f, 0.1f, 0.3f, 0.45f, 0.6f, 0.75f, 0.9f, 0.98f];
        return XisfImageData.FromSamples(values, [4, 2], 1);
    }

    public static XisfDisplayFunction Stored() => new(
        [0.2, 0.3, 0.4, 0.5], [0, 0, 0, 0], [0.8, 0.9, 1, 1], [0, 0, 0, 0], [1, 1, 1, 1]);

    public static float[] Pixels(ScientificImageDisplay display)
    {
        float[] pixels = new float[checked(display.Width * display.Height * 4)];
        for (int y = 0; y < display.Height; y++)
            display.CopyRgbaRow(y, pixels.AsSpan(y * display.Width * 4, display.Width * 4));
        return pixels;
    }

    public static double MedianLight(float[] rgba)
    {
        double[] samples = Enumerable.Range(0, rgba.Length / 4).Where(p => rgba[p * 4 + 3] > 0)
            .Select(p => (rgba[p * 4] + rgba[p * 4 + 1] + rgba[p * 4 + 2]) / 3d).Order().ToArray();
        return samples[samples.Length / 2];
    }
}
