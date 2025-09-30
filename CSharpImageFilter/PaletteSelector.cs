using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;

namespace CSharpImageFilter;

// (This record might be in your file already, ensure it's present)
public record HslColor(float H, float S, float L)
{
    public float H { get; set; } = H;
    public float S { get; set; }  = S;
    public float L { get; set; }  = L;
};

public static class PaletteSelector
{

    /// <summary>
    /// Extracts the most dominant colors from an image using k-means clustering in the L*a*b* space.
    /// This is perceptually more accurate than clustering in RGB.
    /// </summary>
    public static List<Rgba32> GetDominantColors(Image<Rgba32> image, int k)
    {
        // 1. Convert all relevant image pixels from RGBA to LAB.
        var labPixels = new List<Lab>();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                foreach (var pixel in accessor.GetRowSpan(y))
                {
                    if (pixel.A > 128) // Only consider opaque pixels
                    {
                        labPixels.Add(ColorConversion.ToLab(pixel));
                    }
                }
            }
        });

        if (labPixels.Count == 0) return new List<Rgba32>();
        if (labPixels.Count < k) k = labPixels.Count;


        // 2. K-means clustering directly on the L*a*b* color values.
        var random = new Random();
        var centroids = labPixels.OrderBy(x => random.Next()).Take(k).ToList();
        var assignments = new int[labPixels.Count];

        for (int i = 0; i < 15; i++) // Iterate a few times for convergence
        {
            bool changed = false;
            // Assign each pixel to the closest centroid
            for (int j = 0; j < labPixels.Count; j++)
            {
                double minDistance = double.MaxValue;
                int bestCentroid = 0;
                for (int c = 0; c < k; c++)
                {
                    // The key change: Distance is now calculated in L*a*b* space!
                    double distance = GetLabDistanceSquared(labPixels[j], centroids[c]);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestCentroid = c;
                    }
                }
                if (assignments[j] != bestCentroid)
                {
                    assignments[j] = bestCentroid;
                    changed = true;
                }
            }
            if (!changed) break; // Converged

            // Recalculate centroids
            var newCentroids = new Lab[k];
            var counts = new int[k];
            for (int j = 0; j < labPixels.Count; j++)
            {
                newCentroids[assignments[j]] = newCentroids[assignments[j]] with
                {
                    L = newCentroids[assignments[j]].L + labPixels[j].L,
                    A = newCentroids[assignments[j]].A + labPixels[j].A,
                    B = newCentroids[assignments[j]].B + labPixels[j].B
                };
                counts[assignments[j]]++;
            }

            for (int c = 0; c < k; c++)
            {
                if (counts[c] > 0)
                {
                    centroids[c] = new Lab(
                        newCentroids[c].L / counts[c],
                        newCentroids[c].A / counts[c],
                        newCentroids[c].B / counts[c]
                    );
                }
            }
        }

        // 3. Convert the final L*a*b* centroids back to RGBA to return the palette.
        return centroids.Select(c => ColorConversion.ToRgb(c)).ToList();
    }

    /// <summary>
    /// Finds the closest color in a palette to a target color using CIELAB distance.
    /// </summary>
    public static Rgba32 FindClosestColor(Rgba32 target, List<Rgba32> palette)
    {
        if (palette == null || palette.Count == 0) return target;
        
        var targetLab = ColorConversion.ToLab(target);
        
        Rgba32 bestColor = palette[0];
        double minDistance = double.MaxValue;

        foreach (var color in palette)
        {
            var paletteLab = ColorConversion.ToLab(color);
            double dist = GetLabDistanceSquared(targetLab, paletteLab);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestColor = color;
            }
        }
        return bestColor;
    }
    
    /// <summary>
    /// Calculates the squared Euclidean distance between two L*a*b* colors.
    /// </summary>
    private static double GetLabDistanceSquared(Lab color1, Lab color2)
    {
        double dL = color1.L - color2.L;
        double dA = color1.A - color2.A;
        double dB = color1.B - color2.B;
        return dL * dL + dA * dA + dB * dB;
    }

    /// <summary>
    /// Converts an Rgba32 color to the HSL color space (used for palette categorization).
    /// </summary>
    public static HslColor RgbToHsl(Rgba32 rgba)
    {
        var hsl = ColorSpaceConverter.ToHsl(rgba);
        return new HslColor(hsl.H, hsl.S, hsl.L);
    }
}