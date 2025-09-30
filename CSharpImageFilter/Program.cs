using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Numerics;

namespace CSharpImageFilter;

// Helper record to store the result of a channel's sub-pixel combination
public record ChannelCombination(Vector3 ColorSum, Rgba32[] Pixels);

// NEW: A central place to control the dithering quality and color count.
public record DitheringConfig(
    int RedShades,     // How many shades of red to use (including black)
    int GreenShades,   // How many shades of green to use (including black)
    int BlueShades,    // How many shades of blue to use (including black)
    int DominantColorsToFind, // How many initial colors to sample from the image
    int BeamWidth      // The width for the beam search algorithm
);

// NEW: An enum for clarity when working with color channels.
public enum ColorChannel { Red, Green, Blue }

class Program
{
    static void Main()
    {
        string inputPath = "input.png";
        string outputPath = "output.png";
        int downscaleFactor = 9;

        // --- NEW CONFIGURATION "CONTROL PANEL" ---
        // Here you can easily adjust the quality vs. color count tradeoff.
        var config = new DitheringConfig(
            RedShades: 5,      // #Shades including black
            GreenShades: 5,    
            BlueShades: 4,     
            DominantColorsToFind: 16, // More colors can lead to a better palette choice
            BeamWidth: 1024
        );
        // -----------------------------------------


        using var input = Image.Load<Rgba32>(inputPath);

        var fullPalette = new List<Rgba32>
        {
            //Rgba32.ParseHex("#3c3c3c"), Rgba32.ParseHex("#787878"), Rgba32.ParseHex("#aaaaaa"), Rgba32.ParseHex("#d2d2d2"), Rgba32.ParseHex("#ffffff"),
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

        var selectedPalette = SelectBestPalette(input, fullPalette, config);

        Console.WriteLine("Selected Red Shades: " + string.Join(", ", selectedPalette[ColorChannel.Red].Select(c => c.ToHex())));
        Console.WriteLine("Selected Green Shades: " + string.Join(", ", selectedPalette[ColorChannel.Green].Select(c => c.ToHex())));
        Console.WriteLine("Selected Blue Shades: " + string.Join(", ", selectedPalette[ColorChannel.Blue].Select(c => c.ToHex())));

        // --- PRE-COMPUTATION STEP ---
        // Generate all possible color combinations using the new dynamic palettes
        Console.WriteLine("Generating channel combinations...");
        var rCombs = GenerateChannelCombinations(selectedPalette[ColorChannel.Red].ToArray(), 3);
        var gCombs = GenerateChannelCombinations(selectedPalette[ColorChannel.Green].ToArray(), 2);
        var bCombs = GenerateChannelCombinations(selectedPalette[ColorChannel.Blue].ToArray(), 4);
        
        Console.WriteLine($"Total combinations to check per pixel: R={rCombs.Count}, G={gCombs.Count}, B={bCombs.Count}");


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

        Console.WriteLine($"Finding best blocks for {uniqueColors.Count} unique colors...");
        Parallel.ForEach(uniqueColors, color =>
        {
            // Pass the config's beam width to the computation function
            colorToBlockDict[color] = ComputeBestBlock(color, rCombs, gCombs, bCombs, config.BeamWidth);
        });

        // --- IMAGE RECONSTRUCTION ---
        // (The rest of this method from here down remains unchanged)
        Console.WriteLine("Reconstructing final image...");
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
    /// Selects the best palette colors for each channel based on the image content and configuration.
    /// </summary>
    static Dictionary<ColorChannel, List<Rgba32>> SelectBestPalette(
        Image<Rgba32> input,
        List<Rgba32> fullPalette,
        DitheringConfig config)
    {
        Console.WriteLine($"Selecting best palette from {config.DominantColorsToFind} dominant colors...");
        var dominantColors = PaletteSelector.GetDominantColors(input, config.DominantColorsToFind);

        var redishPalette = fullPalette.Where(c => IsRedish(c)).ToList();
        var greenishPalette = fullPalette.Where(c => IsGreenish(c)).ToList();
        var blueishPalette = fullPalette.Where(c => IsBlueish(c)).ToList();

        var colorScores = new Dictionary<Rgba32, float>();

        foreach (var color in dominantColors)
        {
            List<Rgba32> targetPalette;
            if (IsRedish(color)) targetPalette = redishPalette;
            else if (IsGreenish(color)) targetPalette = greenishPalette;
            else if (IsBlueish(color)) targetPalette = blueishPalette;
            else continue;

            if (!targetPalette.Any()) continue;

            var closest = PaletteSelector.FindClosestColor(color, targetPalette);
            if (colorScores.ContainsKey(closest))
                colorScores[closest]++;
            else
                colorScores[closest] = 1;
        }

        // Select the top N scored colors for each channel based on the config
        var finalRed = colorScores.Where(kvp => redishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(config.RedShades - 1).ToList();
        var finalGreen = colorScores.Where(kvp => greenishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(config.GreenShades - 1).ToList();
        var finalBlue = colorScores.Where(kvp => blueishPalette.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key).Take(config.BlueShades - 1).ToList();

        // Fill any empty slots if not enough colors were scored from the dominant set
        while (finalRed.Count < config.RedShades - 1 && redishPalette.Count > finalRed.Count)
            finalRed.Add(redishPalette.First(c => !finalRed.Contains(c)));
        while (finalGreen.Count < config.GreenShades - 1 && greenishPalette.Count > finalGreen.Count)
            finalGreen.Add(greenishPalette.First(c => !finalGreen.Contains(c)));
        while (finalBlue.Count < config.BlueShades - 1 && blueishPalette.Count > finalBlue.Count)
            finalBlue.Add(blueishPalette.First(c => !finalBlue.Contains(c)));

        var black = Rgba32.ParseHex("#000000");

        // Add black to each list, as it's a fundamental part of the combinations
        finalRed.Insert(0, black);
        finalGreen.Insert(0, black);
        finalBlue.Insert(0, black);

        return new Dictionary<ColorChannel, List<Rgba32>>
        {
            { ColorChannel.Red, finalRed },
            { ColorChannel.Green, finalGreen },
            { ColorChannel.Blue, finalBlue }
        };
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

    static Rgba32[,] ComputeBestBlock(
        Rgba32 pixel,
        List<ChannelCombination> rCombs,
        List<ChannelCombination> gCombs,
        List<ChannelCombination> bCombs,
        int beamWidth = 1024)
    {
        var targetTotalSum = new Vector3(pixel.R, pixel.G, pixel.B) * 9f;

        // --- The Beam Search heuristic can remain in fast RGB space ---
        var targetRSum = targetTotalSum * (3f / 9f);
        var bestRs = rCombs
            .OrderBy(r => Vector3.DistanceSquared(r.ColorSum, targetRSum))
            .Take(beamWidth)
            .ToList();

        var targetRGSum = targetTotalSum * (5f / 9f);
        var candidateRGs = new List<(ChannelCombination R, ChannelCombination G, Vector3 Sum)>();
        foreach (var r in bestRs)
            foreach (var g in gCombs)
                candidateRGs.Add((r, g, r.ColorSum + g.ColorSum));

        var topRGs = candidateRGs
            .OrderBy(rg => Vector3.DistanceSquared(rg.Sum, targetRGSum))
            .Take(beamWidth)
            .ToList();

        // --- STEP 3: Finalize with Blue using a perceptually accurate L*a*b* error metric ---

        // THE KEY CHANGE IS HERE: We convert the target pixel to L*a*b* ONCE for comparison.
        var targetLab = ColorConversion.ToLab(pixel);

        ChannelCombination bestR = rCombs[0];
        ChannelCombination bestG = gCombs[0];
        ChannelCombination bestB = bCombs[0];
        float minError = float.MaxValue;

        foreach (var rg in topRGs)
        {
            foreach (var b in bCombs)
            {
                var totalSum = rg.Sum + b.ColorSum;

                // Create the average color of the 3x3 block
                var averageBlockColor = new Rgba32(
                    (byte)(totalSum.X / 9f),
                    (byte)(totalSum.Y / 9f),
                    (byte)(totalSum.Z / 9f)
                );

                // Convert the block's average color to L*a*b*
                var blockLab = ColorConversion.ToLab(averageBlockColor);

                // Calculate error in L*a*b* space. This is the crucial improvement.
                float error = Vector3.DistanceSquared(
                    new Vector3(blockLab.L, blockLab.A, blockLab.B),
                    new Vector3(targetLab.L, targetLab.A, targetLab.B)
                );

                if (error < minError)
                {
                    minError = error;
                    bestR = rg.R;
                    bestG = rg.G;
                    bestB = b;
                }
            }
        }

        // --- Fill the Block (this part is unchanged) ---
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
