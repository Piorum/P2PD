using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Numerics;

namespace CSharpImageFilter;

/// <summary>
/// Represents a pre-calculated combination of 4 pixels from the palette.
/// Caching the LabPixels is an optimization to avoid recalculating them.
/// </summary>
public record PixelCombination(Vector3 ColorSum, Rgba32[] Pixels, Lab[] LabPixels);

/// <summary>
/// Holds all configuration for the image filtering process.
/// </summary>
public record DitheringConfig(int PaletteSize, float LuminanceWeight, float BrightnessBias, float Smoothness);


/// <summary>
/// A self-contained static class that performs the image filtering algorithm.
/// </summary>
public static class ImageProcessor
{
    /// <summary>
    /// Processes an input image using a configurable dithering algorithm and saves the result.
    /// </summary>
    public static void ProcessImage(string inputPath, string outputPath, int downscaleFactor, DitheringConfig config)
    {
        using var input = Image.Load<Rgba32>(inputPath);

        var unifiedPalette = SelectBestPalette(input, config).ToArray();
        Console.WriteLine("Selected Unified Palette: " + string.Join(", ", unifiedPalette.Select(c => c.ToHex())));

        Console.WriteLine("Generating pixel combinations...");
        var combinations = GenerateCombinations(unifiedPalette, 4);
        Console.WriteLine($"Total combinations to check per pixel: {combinations.Count}");

        Console.WriteLine("Pre-calculating Lab conversions for average colors...");
        var avgColorLabCache = combinations
            .Select(c => new Rgba32((byte)(c.ColorSum.X / 4f), (byte)(c.ColorSum.Y / 4f), (byte)(c.ColorSum.Z / 4f)))
            .Distinct()
            .AsParallel()
            .ToDictionary(c => c, ColorConversion.ToLab);
        Console.WriteLine($"Cached {avgColorLabCache.Count} unique average colors.");

        Console.WriteLine("Downscaling image...");
        int newW = input.Width / downscaleFactor;
        int newH = input.Height / downscaleFactor;
        var downscaled = HardDownscale(input, downscaleFactor);

        var uniqueColors = new HashSet<Rgba32>();
        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
        {
            if (downscaled[x, y].A > 0)
            {
                uniqueColors.Add(downscaled[x, y]);
            }
        }

        var colorToBlockDict = new ConcurrentDictionary<Rgba32, Rgba32[,]>();
        Console.WriteLine($"Finding best blocks for {uniqueColors.Count} unique colors (exhaustive search)...");
        Parallel.ForEach(uniqueColors, color =>
        {
            colorToBlockDict[color] = ComputeBestBlock(color, combinations, avgColorLabCache, config);
        });

        Console.WriteLine("Reconstructing final image...");
        int outW = newW * 2;
        int outH = newH * 2;
        using var output = new Image<Rgba32>(outW, outH);

        for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                var px = downscaled[x, y];
                if (px.A == 0) continue;
                var block = colorToBlockDict[px];

                for (int row = 0; row < 2; row++)
                    for (int col = 0; col < 2; col++)
                        output[x * 2 + col, y * 2 + row] = block[row, col];
            }

        output.Save(outputPath);
        Console.WriteLine("Done.");
    }

    private static List<Rgba32> SelectBestPalette(Image<Rgba32> input, DitheringConfig config)
    {
        Console.WriteLine($"Generating a globally optimized palette of {config.PaletteSize} colors...");
        var optimizedPalette = PaletteSelector.GetOptimizedPalette(input, config.PaletteSize);

        if (Math.Abs(config.BrightnessBias - 1.0f) > 0.001f)
        {
            for (int i = 0; i < optimizedPalette.Count; i++)
            {
                var lab = ColorConversion.ToLab(optimizedPalette[i]);
                var biasedLab = new Lab(lab.L * config.BrightnessBias, lab.A, lab.B);
                optimizedPalette[i] = ColorConversion.ToRgb(biasedLab);
            }
        }
        return optimizedPalette.Distinct().ToList();
    }

    private static List<PixelCombination> GenerateCombinations(Rgba32[] palette, int count)
    {
        var results = new List<PixelCombination>();
        var distinctPalette = palette.Distinct().ToArray();
        var labCache = distinctPalette.ToDictionary(c => c, ColorConversion.ToLab);

        void Recurse(int depth, List<Rgba32> currentCombo)
        {
            if (depth == count)
            {
                var sum = new Vector3(currentCombo.Sum(c => c.R), currentCombo.Sum(c => c.G), currentCombo.Sum(c => c.B));
                var labPixels = currentCombo.Select(c => labCache[c]).ToArray();
                results.Add(new PixelCombination(sum, [.. currentCombo], labPixels));
                return;
            }

            foreach (var shade in distinctPalette)
            {
                currentCombo.Add(shade);
                Recurse(depth + 1, currentCombo);
                currentCombo.RemoveAt(currentCombo.Count - 1);
            }
        }

        Recurse(0, new List<Rgba32>());
        return results;
    }

    private static Rgba32[,] ComputeBestBlock(Rgba32 pixel, List<PixelCombination> combinations, Dictionary<Rgba32, Lab> avgColorLabCache, DitheringConfig config)
    {
        var targetLab = ColorConversion.ToLab(pixel);
        PixelCombination bestCombination = combinations[0];
        float minError = float.MaxValue;

        foreach (var combination in combinations)
        {
            var totalSum = combination.ColorSum;
            var averageBlockColor = new Rgba32((byte)(totalSum.X / 4f), (byte)(totalSum.Y / 4f), (byte)(totalSum.Z / 4f));
            
            var blockLab = avgColorLabCache[averageBlockColor]; // OPTIMIZED: Fast cache lookup

            float dL = targetLab.L - blockLab.L;
            float dA = targetLab.A - blockLab.A;
            float dB = targetLab.B - blockLab.B;
            float matchError = (dL * config.LuminanceWeight) * (dL * config.LuminanceWeight) + (dA * dA) + (dB * dB);

            float variance = 0;
            if (config.Smoothness > 0)
            {
                variance += GetLabDistanceSquared(combination.LabPixels[0], blockLab);
                variance += GetLabDistanceSquared(combination.LabPixels[1], blockLab);
                variance += GetLabDistanceSquared(combination.LabPixels[2], blockLab);
                variance += GetLabDistanceSquared(combination.LabPixels[3], blockLab);
                variance /= 4f;
            }
            
            float totalError = matchError + (variance * config.Smoothness);

            if (totalError < minError)
            {
                minError = totalError;
                bestCombination = combination;
            }
        }

        return new Rgba32[,]
        {
            { bestCombination.Pixels[0], bestCombination.Pixels[1] },
            { bestCombination.Pixels[2], bestCombination.Pixels[3] },
        };
    }

    private static float GetLabDistanceSquared(Lab color1, Lab color2)
    {
        float dL = color1.L - color2.L;
        float dA = color1.A - color2.A;
        float dB = color1.B - color2.B;
        return dL * dL + dA * dA + dB * dB;
    }

    private static Image<Rgba32> HardDownscale(Image<Rgba32> input, int factor)
    {
        int newW = input.Width / factor;
        int newH = input.Height / factor;
        var result = new Image<Rgba32>(newW, newH);

        for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                float rSum = 0, gSum = 0, bSum = 0, aSum = 0;
                int count = 0;

                for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        var px = input[x * factor + dx, y * factor + dy];
                        rSum += px.R;
                        gSum += px.G;
                        bSum += px.B;
                        aSum += px.A;
                        count++;
                    }

                result[x, y] = new Rgba32((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count), (byte)(aSum / count));
            }
        return result;
    }
}
