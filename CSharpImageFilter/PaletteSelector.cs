using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp.Processing;
using System.Collections.Generic;
using System.Linq;

namespace CSharpImageFilter;

// HslColor record remains the same...
public record HslColor(float H, float S, float L)
{
    public float H { get; set; } = H;
    public float S { get; set; }  = S;
    public float L { get; set; }  = L;
};

public static class PaletteSelector
{
    /// <summary>
    /// Analyzes an image and selects the N most representative colors from a larger master palette.
    /// </summary>
    /// <param name="image">The source image to analyze.</param>
    /// <param name="masterPalette">The complete list of allowed colors.</param>
    /// <param name="subsetSize">The target number of colors to select.</param>
    /// <returns>A smaller list of the most relevant colors for the image.</returns>
    public static List<Rgba32> SelectBestSubsetFromPalette(Image<Rgba32> image, List<Rgba32> masterPalette, int subsetSize)
    {
        if (masterPalette == null || masterPalette.Count <= subsetSize)
        {
            return masterPalette!;
        }

        Console.WriteLine($"Selecting the best {subsetSize} colors from the master palette of {masterPalette.Count}...");

        // Create a "vote count" for each color in the master palette.
        var colorScores = masterPalette.ToDictionary(color => color, color => 0L);

        // For each pixel in the image, find its closest color in the master palette and cast a "vote".
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                Rgba32 pixelColor = image[x, y];
                // We only care about non-transparent pixels for this analysis
                if (pixelColor.A > 0)
                {
                    Rgba32 closestPaletteColor = FindClosestColor(pixelColor, masterPalette);
                    colorScores[closestPaletteColor]++;
                }
            }
        }

        // Order the colors by their score (number of votes) in descending order.
        var sortedColors = colorScores.OrderByDescending(kvp => kvp.Value);

        // Take the top N colors to form our new, optimized palette.
        var finalPalette = sortedColors.Take(subsetSize).Select(kvp => kvp.Key).ToList();
        
        Console.WriteLine("Color subset selection complete.");
        return finalPalette;
    }

    // GetOptimizedPalette remains the same...
    public static List<Rgba32> GetOptimizedPalette(Image<Rgba32> image, int paletteSize)
    {
        var quantizerFactory = new OctreeQuantizer(new QuantizerOptions { MaxColors = paletteSize });
        IQuantizer<Rgba32> quantizer = quantizerFactory.CreatePixelSpecificQuantizer<Rgba32>(image.Configuration);
        var pixelSamplingStrategy = new DefaultPixelSamplingStrategy();
        quantizer.BuildPalette(pixelSamplingStrategy, image);
        ReadOnlyMemory<Rgba32> palette = quantizer.Palette;
        return palette.Span.ToArray().ToList();
    }

    // FindClosestColor remains the same...
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

    // GetLabDistanceSquared remains the same...
    private static double GetLabDistanceSquared(Lab color1, Lab color2)
    {
        double dL = color1.L - color2.L;
        double dA = color1.A - color2.A;
        double dB = color1.B - color2.B;
        return dL * dL + dA * dA + dB * dB;
    }

    // RgbToHsl remains the same...
    public static HslColor RgbToHsl(Rgba32 rgba)
    {
        var hsl = ColorSpaceConverter.ToHsl(rgba);
        return new HslColor(hsl.H, hsl.S, hsl.L);
    }
}