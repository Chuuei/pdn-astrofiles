namespace PdnAstroFiles.Imaging;

public sealed partial class ScientificImageDisplay
{
    private readonly record struct BackgroundPlane(double X, double Y)
    {
        public bool IsActive => X != 0 || Y != 0;
        public double At(int x, int y, int width, int height) =>
            X * ((x + 0.5) / width - 0.5) + Y * ((y + 0.5) / height - 0.5);
    }

    // Adapted from SkyBrowser's clipped background fit (Apache-2.0, Copyright 2026
    // Chuuei and contributors). Samples come directly from the normalized scientific
    // image, including CFA reconstruction, rather than a downsampled preview buffer.
    private BackgroundPlane EstimateGradient(SampleReader reader, int channel)
    {
        int width = source.Dimensions[0], height = source.Dimensions[1];
        if (width < 96 || height < 96) return default;
        const int grid = 12, samples = 16;
        var cells = new List<(double X, double Y, double Value)>();
        for (int row = 0; row < grid; row++)
        for (int col = 0; col < grid; col++)
        {
            var values = new List<double>();
            int x0 = col * width / grid, x1 = (col + 1) * width / grid;
            int y0 = row * height / grid, y1 = (row + 1) * height / grid;
            int nx = Math.Min(samples, x1 - x0), ny = Math.Min(samples, y1 - y0);
            for (int sy = 0; sy < ny; sy++)
            for (int sx = 0; sx < nx; sx++)
            {
                int x = x0 + (2 * sx + 1) * (x1 - x0) / (2 * nx);
                int y = y0 + (2 * sy + 1) * (y1 - y0) / (2 * ny);
                int p = y * width + x;
                double value = reader.Read(p, channel);
                if ((!hasAlpha || Unit(reader.ReadAlpha(p)) > 0) && double.IsFinite(value)) values.Add(value);
            }
            if (values.Count >= nx * ny * 3 / 4)
                cells.Add(((x0 + x1) / (2d * width) - 0.5, (y0 + y1) / (2d * height) - 0.5, Median(values.ToArray())));
        }
        if (cells.Count < grid * grid * 3 / 4) return default;

        bool[] included = Enumerable.Repeat(true, cells.Count).ToArray();
        double[] coefficients = new double[3];
        double scatter = 0;
        for (int iteration = 0; iteration < 6; iteration++)
        {
            var matrix = new double[3, 4];
            for (int i = 0; i < cells.Count; i++)
            {
                if (!included[i]) continue;
                var cell = cells[i];
                double[] basis = [1, cell.X, cell.Y];
                for (int r = 0; r < 3; r++)
                {
                    for (int c = 0; c < 3; c++) matrix[r, c] += basis[r] * basis[c];
                    matrix[r, 3] += basis[r] * cell.Value;
                }
            }
            for (int pivot = 0; pivot < 3; pivot++)
            {
                double divisor = matrix[pivot, pivot];
                if (Math.Abs(divisor) < 1e-10) return default;
                for (int c = pivot; c < 4; c++) matrix[pivot, c] /= divisor;
                for (int r = 0; r < 3; r++)
                {
                    if (r == pivot) continue;
                    double factor = matrix[r, pivot];
                    for (int c = pivot; c < 4; c++) matrix[r, c] -= factor * matrix[pivot, c];
                }
            }
            for (int c = 0; c < 3; c++) coefficients[c] = matrix[c, 3];
            double[] residuals = cells.Select(cell => cell.Value - coefficients[0] - coefficients[1] * cell.X - coefficients[2] * cell.Y).ToArray();
            double center = Median(residuals.Where((_, i) => included[i]).ToArray());
            scatter = 1.4826 * Median(residuals.Where((_, i) => included[i]).Select(value => Math.Abs(value - center)).ToArray());
            if (iteration == 5) break;
            double limit = Math.Max(1e-10, 2.5 * scatter);
            for (int i = 0; i < cells.Count; i++) included[i] &= Math.Abs(residuals[i] - center) <= limit;
            if (included.Count(value => value) < cells.Count * 0.6) return default;
        }
        // Broad support and rejection on both sides protect emission and dark clouds.
        for (int quadrant = 0; quadrant < 4; quadrant++)
            if (cells.Where((cell, i) => included[i] && (cell.X >= 0 ? 1 : 0) + (cell.Y >= 0 ? 2 : 0) == quadrant).Count() < 8)
                return default;
        double span = Math.Abs(coefficients[1]) + Math.Abs(coefficients[2]);
        return !double.IsFinite(span) || !double.IsFinite(scatter) || span < Math.Max(1e-8, 2 * scatter)
            ? default : new(coefficients[1] * 0.8, coefficients[2] * 0.8);
    }
}
