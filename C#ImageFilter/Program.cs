using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Numerics;

namespace CSharpImageFilter;

// Helper record to store the result of a channel's sub-pixel combination
public record ChannelCombination(Vector3 ColorSum, Rgba32[] Pixels);

class Program
{
    static void Main()
    {
        string inputPath = "input.png";
        string outputPath = "output.png";
        int downscaleFactor = 3;

        using var input = Image.Load<Rgba32>(inputPath);

        var fullPalette = new List<Rgba32>
        {
            Rgba32.ParseHex("#600018"), Rgba32.ParseHex("#a50e1e"), Rgba32.ParseHex("#ed1c24"),
            Rgba32.ParseHex("#fa8072"), Rgba32.ParseHex("#e45c1a"), Rgba32.ParseHex("#ff7f27"),
            Rgba32.ParseHex("#f6aa09"), Rgba32.ParseHex("#f9dd3b"), Rgba32.ParseHex("#fffabc"),
            Rgba32.ParseHex("#9c8431"), Rgba32.ParseHex("#c5ad31"), Rgba32.ParseHex("#e8d45f"),
            Rgba32.ParseHex("#4a6b3a"), Rgba32.ParseHex("#5a944a"), Rgba32.ParseHex("#84c573"),
            Rgba32.ParseHex("#0eb968"), Rgba32.ParseHex("#13e67b"), Rgba32.ParseHex("#87ff5e"),
            Rgba32.ParseHex("#0c816e"), Rgba32.ParseHex("#10aea6"), Rgba32.ParseHex("#13e1be"),
            Rgba32.ParseHex("#0f799f"), Rgba32.ParseHex("#60f7f2"), Rgba32.ParseHex("#bbfaf2"),
            Rgba32.ParseHex("#28509e"), Rgba32.ParseHex("#4093e4"), Rgba32.ParseHex("#7dc7ff"),
            Rgba32.ParseHex("#4d31b8"), Rgba32.ParseHex("#6b50f6"), Rgba32.ParseHex("#99b1fb"),
            Rgba32.ParseHex("#4a4284"), Rgba32.ParseHex("#7a71c4"), Rgba32.ParseHex("#b5aef1"),
            Rgba32.ParseHex("#780c99"), Rgba32.ParseHex("#aa38b9"), Rgba32.ParseHex("#e09ff9"),
            Rgba32.ParseHex("#cb007a"), Rgba32.ParseHex("#ec1f80"), Rgba32.ParseHex("#f38da9"),
            Rgba32.ParseHex("#9b5249"), Rgba32.ParseHex("#d18078"), Rgba32.ParseHex("#fab6a4"),
            Rgba32.ParseHex("#684634"), Rgba32.ParseHex("#95682a"), Rgba32.ParseHex("#dba463"),
            Rgba32.ParseHex("#7b6352"), Rgba32.ParseHex("#9c846b"), Rgba32.ParseHex("#d6b594"),
            Rgba32.ParseHex("#d18051"), Rgba32.ParseHex("#f8b277"), Rgba32.ParseHex("#ffc5a5"),
            Rgba32.ParseHex("#6d643f"), Rgba32.ParseHex("#948c6b"), Rgba32.ParseHex("#cdc59e"),
            Rgba32.ParseHex("#333941"), Rgba32.ParseHex("#6d758d"), Rgba32.ParseHex("#b3b9d1"),
        };

        var finalPalette = SelectBestPalette(input, fullPalette);

        // --- PRE-COMPUTATION STEP ---
        // Generate all possible color combinations for each channel's sub-pixels
        var rCombs = GenerateChannelCombinations(
            [finalPalette.Black, finalPalette.RedDark, finalPalette.RedNormal, finalPalette.RedLight], 3);
        var gCombs = GenerateChannelCombinations(
            [finalPalette.Black, finalPalette.GreenDark, finalPalette.GreenNormal, finalPalette.GreenLight], 2);
        var bCombs = GenerateChannelCombinations(
            [finalPalette.Black, finalPalette.BlueDark, finalPalette.BlueNormal, finalPalette.BlueLight], 4);

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

        // Cache of full 3x3 blocks for each unique color// ... in your Main method

        // Change this from Dictionary to ConcurrentDictionary for thread safety
        var colorToBlockDict = new ConcurrentDictionary<Rgba32, Rgba32[,]>();

        // Replace the standard foreach loop with Parallel.ForEach
        Parallel.ForEach(uniqueColors, color =>
        {
            // Use the new Hill Climbing function
            colorToBlockDict[color] = ComputeBestBlock(color, rCombs, gCombs, bCombs);
        });

        // ... the rest of your code remains the same

        // --- IMAGE RECONSTRUCTION ---
        int outW = newW * 3;
        int outH = newH * 3;
        using var output = new Image<Rgba32>(outW, outH);

        for (int y = 0; y < newH; y++)
        for (int x = 0; x < newW; x++)
        {
            var px = downscaled[x, y];
            if (px.A == 0) continue;
            var block = colorToBlockDict[px];

            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
                output[x * 3 + col, y * 3 + row] = block[row, col];
        }

        output.Save(outputPath);
        Console.WriteLine("Done.");
    }

    /// <summary>
    /// Selects the 9 best palette colors based on the image content using hue categorization.
    /// </summary>
    static Palette SelectBestPalette(Image<Rgba32> input, List<Rgba32> fullPalette)
    {
        var dominantColors = PaletteSelector.GetDominantColors(input, 16);

        // Categorize the full palette based on hue
        var redishPalette = fullPalette.Where(c => IsRedish(c)).ToList();
        var greenishPalette = fullPalette.Where(c => IsGreenish(c)).ToList();
        var blueishPalette = fullPalette.Where(c => IsBlueish(c)).ToList();

        var colorScores = new Dictionary<Rgba32, float>();

        // Score palette colors based on their proximity to the dominant image colors
        foreach (var color in dominantColors)
        {
            List<Rgba32> targetPalette;
            if (IsRedish(color)) targetPalette = redishPalette;
            else if (IsGreenish(color)) targetPalette = greenishPalette;
            else targetPalette = blueishPalette;
            
            if (!targetPalette.Any()) continue;

            var closest = PaletteSelector.FindClosestColor(color, targetPalette);
            if (colorScores.ContainsKey(closest))
                colorScores[closest]++;
            else
                colorScores[closest] = 1;
        }
        
        var finalRed = colorScores.Where(kvp => redishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(3).ToList();
        var finalGreen = colorScores.Where(kvp => greenishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(3).ToList();
        var finalBlue = colorScores.Where(kvp => blueishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(3).ToList();

        // Fill any empty slots if not enough colors were scored
        while (finalRed.Count < 3 && redishPalette.Count > finalRed.Count)
            finalRed.Add(redishPalette.First(c => !finalRed.Contains(c)));
        while (finalGreen.Count < 3 && greenishPalette.Count > finalGreen.Count)
            finalGreen.Add(greenishPalette.First(c => !finalGreen.Contains(c)));
        while (finalBlue.Count < 3 && blueishPalette.Count > finalBlue.Count)
            finalBlue.Add(blueishPalette.First(c => !finalBlue.Contains(c)));

        return new Palette(
            Black: Rgba32.ParseHex("#000000"),
            RedDark: finalRed.Count > 0 ? finalRed[0] : new Rgba32(0,0,0),
            RedNormal: finalRed.Count > 1 ? finalRed[1] : new Rgba32(0,0,0),
            RedLight: finalRed.Count > 2 ? finalRed[2] : new Rgba32(0,0,0),
            GreenDark: finalGreen.Count > 0 ? finalGreen[0] : new Rgba32(0,0,0),
            GreenNormal: finalGreen.Count > 1 ? finalGreen[1] : new Rgba32(0,0,0),
            GreenLight: finalGreen.Count > 2 ? finalGreen[2] : new Rgba32(0,0,0),
            BlueDark: finalBlue.Count > 0 ? finalBlue[0] : new Rgba32(0,0,0),
            BlueNormal: finalBlue.Count > 1 ? finalBlue[1] : new Rgba32(0,0,0),
            BlueLight: finalBlue.Count > 2 ? finalBlue[2] : new Rgba32(0,0,0)
        );
    }
    
    // Hue-based color categorization
    static bool IsRedish(Rgba32 c) => PaletteSelector.RgbToHsl(c).H is >= 330 or < 30;
    static bool IsGreenish(Rgba32 c) => PaletteSelector.RgbToHsl(c).H is >= 70 and < 160;
    static bool IsBlueish(Rgba32 c) => PaletteSelector.RgbToHsl(c).H is >= 180 and < 270;

    /// <summary>
    /// Pre-calculates all possible color sums for a given set of shades and pixel count.
    /// </summary>
    static List<ChannelCombination> GenerateChannelCombinations(Rgba32[] shades, int count)
    {
        var results = new List<ChannelCombination>();

        void Recurse(int depth, List<Rgba32> currentCombo)
        {
            if (depth == count)
            {
                var sum = new Vector3(
                    currentCombo.Sum(c => c.R),
                    currentCombo.Sum(c => c.G),
                    currentCombo.Sum(c => c.B)
                );
                results.Add(new ChannelCombination(sum, [.. currentCombo]));
                return;
            }

            foreach (var shade in shades)
            {
                currentCombo.Add(shade);
                Recurse(depth + 1, currentCombo);
                currentCombo.RemoveAt(currentCombo.Count - 1);
            }
        }

        Recurse(0, new List<Rgba32>());
        return results;
    }

    /// <summary>
    /// Finds the best combination using Beam Search.
    /// Explores the top 'beamWidth' candidates at each step, offering a great balance of speed and quality.
    /// </summary>
    static Rgba32[,] ComputeBestBlock(
        Rgba32 pixel,
        List<ChannelCombination> rCombs,
        List<ChannelCombination> gCombs,
        List<ChannelCombination> bCombs,
        int beamWidth = 1024) // A width of 20-50 is usually a good sweet spot
    {
        // We are trying to match the total sum of all 9 sub-pixels
        var targetTotalSum = new Vector3(pixel.R, pixel.G, pixel.B) * 9f;

        // --- STEP 1: Find the Top K Red Candidates ---
        // The 3 red sub-pixels should ideally contribute 3/9ths of the total color.
        var targetRSum = targetTotalSum * (3f / 9f);
        
        var bestRs = rCombs
            .OrderBy(r => Vector3.DistanceSquared(r.ColorSum, targetRSum))
            .Take(beamWidth)
            .ToList();

        // --- STEP 2: Expand to Red + Green ---
        // Combine our top Red candidates with all Green possibilities.
        // Together (3 Red + 2 Green), they should contribute 5/9ths of the total color.
        var targetRGSum = targetTotalSum * (5f / 9f);
        var candidateRGs = new List<(ChannelCombination R, ChannelCombination G, Vector3 Sum)>(beamWidth * gCombs.Count);

        foreach(var r in bestRs)
        {
            foreach(var g in gCombs)
            {
                candidateRGs.Add((r, g, r.ColorSum + g.ColorSum));
            }
        }

        // Keep only the top K Red/Green pairs
        var topRGs = candidateRGs
            .OrderBy(rg => Vector3.DistanceSquared(rg.Sum, targetRGSum))
            .Take(beamWidth)
            .ToList();

        // --- STEP 3: Finalize with Blue ---
        // Check all Blue combinations against our surviving Red/Green pairs to find the global winner.
        ChannelCombination bestR = rCombs[0];
        ChannelCombination bestG = gCombs[0];
        ChannelCombination bestB = bCombs[0];
        float minError = float.MaxValue;

        foreach(var rg in topRGs)
        {
            foreach(var b in bCombs)
            {
                var totalSum = rg.Sum + b.ColorSum;
                float error = Vector3.DistanceSquared(totalSum, targetTotalSum);
                
                if (error < minError)
                {
                    minError = error;
                    bestR = rg.R;
                    bestG = rg.G;
                    bestB = b;
                }
            }
        }

        // --- Fill the Block ---
        Rgba32[,] block = new Rgba32[3, 3];
        block[0, 0] = bestR.Pixels[0];
        block[1, 0] = bestR.Pixels[1];
        block[2, 0] = bestR.Pixels[2];

        block[1, 1] = bestG.Pixels[0];
        block[2, 1] = bestG.Pixels[1];

        block[0, 1] = bestB.Pixels[0];
        block[0, 2] = bestB.Pixels[1];
        block[1, 2] = bestB.Pixels[2];
        block[2, 2] = bestB.Pixels[3];

        return block;
    }
    
    static Image<Rgba32> HardDownscale(Image<Rgba32> input, int factor)
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

            result[x, y] = new Rgba32(
                (byte)(rSum / count), 
                (byte)(gSum / count), 
                (byte)(bSum / count), 
                (byte)(aSum / count));
        }
        return result;
    }
}

public record Palette(
    Rgba32 Black,
    Rgba32 RedDark, Rgba32 RedNormal, Rgba32 RedLight,
    Rgba32 GreenDark, Rgba32 GreenNormal, Rgba32 GreenLight,
    Rgba32 BlueDark, Rgba32 BlueNormal, Rgba32 BlueLight
);