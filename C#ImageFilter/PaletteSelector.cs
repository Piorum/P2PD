using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

// Add this record definition, which is used by the RgbToHsl function.
public record HslColor(float H, float S, float L);

public static class PaletteSelector
{
    /// <summary>
    /// Extracts the most dominant colors from an image using k-means clustering.
    /// </summary>
    public static List<Rgba32> GetDominantColors(Image<Rgba32> image, int k)
    {
        var pixels = new List<Vector3>();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.A > 128) // Only consider opaque pixels
                {
                    pixels.Add(new Vector3(pixel.R, pixel.G, pixel.B));
                }
            }
        }
        
        if (pixels.Count == 0) return new List<Rgba32>();

        // K-means clustering
        var random = new Random();
        var centroids = pixels.OrderBy(x => random.Next()).Take(k).ToList();
        var assignments = new int[pixels.Count];

        for (int i = 0; i < 10; i++) // Iterate a few times for convergence
        {
            // Assign each pixel to the closest centroid
            for (int j = 0; j < pixels.Count; j++)
            {
                double minDistance = double.MaxValue;
                int bestCentroid = 0;
                for (int c = 0; c < k; c++)
                {
                    double distance = Vector3.DistanceSquared(pixels[j], centroids[c]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCentroid = c;
                    }
                }
                assignments[j] = bestCentroid;
            }

            // Recalculate centroids
            var newCentroids = new Vector3[k];
            var counts = new int[k];
            for (int j = 0; j < pixels.Count; j++)
            {
                newCentroids[assignments[j]] += pixels[j];
                counts[assignments[j]]++;
            }

            for (int c = 0; c < k; c++)
            {
                if (counts[c] > 0)
                {
                    centroids[c] = newCentroids[c] / counts[c];
                }
            }
        }

        return centroids.Select(c => new Rgba32((byte)c.X, (byte)c.Y, (byte)c.Z)).ToList();
    }

    /// <summary>
    /// Converts an Rgba32 color to the HSL color space.
    /// H is in degrees [0-360], S and L are [0-1].
    /// </summary>
    public static HslColor RgbToHsl(Rgba32 rgba)
    {
        float r = rgba.R / 255f;
        float g = rgba.G / 255f;
        float b = rgba.B / 255f;

        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float h = 0, s = 0, l = (max + min) / 2;

        if (max == min)
        {
            h = s = 0; // achromatic
        }
        else
        {
            float d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            switch (max)
            {
                case var _ when max == r: h = (g - b) / d + (g < b ? 6 : 0); break;
                case var _ when max == g: h = (b - r) / d + 2; break;
                case var _ when max == b: h = (r - g) / d + 4; break;
            }
            h /= 6;
        }

        return new HslColor(h * 360, s, l);
    }
    
    /// <summary>
    /// Converts an RGB color to the CIELAB color space for perceptually accurate comparisons.
    /// </summary>
    public static Vector3 RgbToLab(Rgba32 rgb)
    {
        // First, convert to XYZ
        double r = rgb.R / 255.0;
        double g = rgb.G / 255.0;
        double b = rgb.B / 255.0;

        r = (r > 0.04045) ? Math.Pow((r + 0.055) / 1.055, 2.4) : r / 12.92;
        g = (g > 0.04045) ? Math.Pow((g + 0.055) / 1.055, 2.4) : g / 12.92;
        b = (b > 0.04045) ? Math.Pow((b + 0.055) / 1.055, 2.4) : b / 12.92;

        r *= 100.0;
        g *= 100.0;
        b *= 100.0;

        double x = r * 0.4124 + g * 0.3576 + b * 0.1805;
        double y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        double z = r * 0.0193 + g * 0.1192 + b * 0.9505;

        // Then, convert XYZ to LAB
        x /= 95.047;
        y /= 100.000;
        z /= 108.883;

        x = (x > 0.008856) ? Math.Pow(x, 1.0 / 3.0) : (7.787 * x) + (16.0 / 116.0);
        y = (y > 0.008856) ? Math.Pow(y, 1.0 / 3.0) : (7.787 * y) + (16.0 / 116.0);
        z = (z > 0.008856) ? Math.Pow(z, 1.0 / 3.0) : (7.787 * z) + (16.0 / 116.0);

        return new Vector3(
            (float)((116.0 * y) - 16.0),
            (float)(500.0 * (x - y)),
            (float)(200.0 * (y - z))
        );
    }

    /// <summary>
    /// Finds the closest color in a palette to a target color using CIELAB distance.
    /// </summary>
    public static Rgba32 FindClosestColor(Rgba32 target, List<Rgba32> palette)
    {
        var targetLab = RgbToLab(target);
        return palette.MinBy(c => Vector3.DistanceSquared(RgbToLab(c), targetLab));
    }
}