using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CSharpImageFilter;

class Program
{
    static void Main()
    {
        string inputPath = "input.png";
        string outputPath = "output.png";
        int downscaleFactor = 9;

        var palette = new Palette(
            Black: Rgba32.ParseHex("#000000"),
            RedDark: Rgba32.ParseHex("#a50e1e"),
            RedNormal: Rgba32.ParseHex("#ed1c24"),
            RedLight: Rgba32.ParseHex("#fa8072"),
            GreenDark: Rgba32.ParseHex("#0eb968"),
            GreenNormal: Rgba32.ParseHex("#13e67b"),
            GreenLight: Rgba32.ParseHex("#87ff5e"),
            BlueDark: Rgba32.ParseHex("#28509e"),
            BlueNormal: Rgba32.ParseHex("#4093e4"),
            BlueLight: Rgba32.ParseHex("#7dc7ff")
        );

        using var input = Image.Load<Rgba32>(inputPath);

        int newW = input.Width / downscaleFactor;
        int newH = input.Height / downscaleFactor;

        var downscaled = HardDownscale(input, downscaleFactor);

        // Collect unique colors
        var uniqueColors = new HashSet<(byte R, byte G, byte B)>();
        for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                var px = downscaled[x, y];
                if (px.A == 0) continue;
                uniqueColors.Add((px.R, px.G, px.B));
            }

        // Dictionaries for caching channel combinations
        var redCache = new Dictionary<int, Rgba32[]>();
        var greenCache = new Dictionary<int, Rgba32[]>();
        var blueCache = new Dictionary<int, Rgba32[]>();

        // Cache of full 3x3 blocks for unique colors
        var colorToBlockDict = new Dictionary<(byte R, byte G, byte B), Rgba32[,]>();

        foreach (var color in uniqueColors)
        {
            var px = new Rgba32(color.R, color.G, color.B);
            colorToBlockDict[color] = ComputeBestBlock(px, palette, redCache, greenCache, blueCache);
        }

        // Output image
        int outW = newW * 3;
        int outH = newH * 3;
        using var output = new Image<Rgba32>(outW, outH);

        for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                var px = downscaled[x, y];
                if (px.A == 0) continue;
                var block = colorToBlockDict[(px.R, px.G, px.B)];

                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        output[x * 3 + col, y * 3 + row] = block[row, col];
            }

        output.Save(outputPath);
        Console.WriteLine("Done.");
    }

    static Image<Rgba32> HardDownscale(Image<Rgba32> input, int factor)
    {
        int newW = input.Width / factor;
        int newH = input.Height / factor;
        var result = new Image<Rgba32>(newW, newH);

        for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                bool anyOpaque = false;
                int rSum = 0, gSum = 0, bSum = 0, count = 0;

                for (int dy = 0; dy < factor; dy++)
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int sx = x * factor + dx;
                        int sy = y * factor + dy;
                        var px = input[sx, sy];
                        if (px.A > 0) anyOpaque = true;
                        rSum += px.R;
                        gSum += px.G;
                        bSum += px.B;
                        count++;
                    }

                if (anyOpaque)
                    result[x, y] = new Rgba32((byte)(rSum / count), (byte)(gSum / count), (byte)(bSum / count), 255);
                else
                    result[x, y] = new Rgba32(0, 0, 0, 0);
            }

        return result;
    }

    static Rgba32[,] ComputeBestBlock(
        Rgba32 pixel,
        Palette palette,
        Dictionary<int, Rgba32[]> redCache,
        Dictionary<int, Rgba32[]> greenCache,
        Dictionary<int, Rgba32[]> blueCache)
    {
        Rgba32[] rShades = [palette.Black, palette.RedDark, palette.RedNormal, palette.RedLight];
        Rgba32[] gShades = [palette.Black, palette.GreenDark, palette.GreenNormal, palette.GreenLight];
        Rgba32[] bShades = [palette.Black, palette.BlueDark, palette.BlueNormal, palette.BlueLight];

        int targetSumR = pixel.R * 3;
        int targetSumG = pixel.G * 2;
        int targetSumB = pixel.B * 4;

        // Look up or compute per-channel assignment
        Rgba32[] rAssigned = redCache.TryGetValue(targetSumR, out Rgba32[]? rValue) ? rValue :
            redCache[targetSumR] = BestCombination(rShades, 3, targetSumR, c => c.R);

        Rgba32[]? gAssigned = greenCache.TryGetValue(targetSumG, out Rgba32[]? gValue) ? gValue :
            greenCache[targetSumG] = BestCombination(gShades, 2, targetSumG, c => c.G);

        Rgba32[]? bAssigned = blueCache.TryGetValue(targetSumB, out Rgba32[]? bValue) ? bValue :
            blueCache[targetSumB] = BestCombination(bShades, 4, targetSumB, c => c.B);

        // Fill block positions (3R/2G/4B)
        Rgba32[,] block = new Rgba32[3, 3];
        block[0, 0] = rAssigned[0];
        block[1, 0] = rAssigned[1];
        block[2, 0] = rAssigned[2];

        block[1, 1] = gAssigned[0];
        block[2, 1] = gAssigned[1];

        block[0, 1] = bAssigned[0];
        block[0, 2] = bAssigned[1];
        block[1, 2] = bAssigned[2];
        block[2, 2] = bAssigned[3];

        return block;
    }

    static Rgba32[] BestCombination(Rgba32[] shades, int count, int targetSum, Func<Rgba32, int> selector)
    {
        // Generate all combinations (small numbers: 3^4=64, 2^4=16, 4^4=256)
        Rgba32[]? bestCombo = null;
        int minError = int.MaxValue;

        void Recurse(int depth, List<Rgba32> current, int currentSum)
        {
            if (depth == count)
            {
                int error = Math.Abs(currentSum - targetSum);
                if (error < minError)
                {
                    minError = error;
                    bestCombo = [.. current];
                }
                return;
            }

            foreach (var shade in shades)
            {
                current.Add(shade);
                Recurse(depth + 1, current, currentSum + selector(shade));
                current.RemoveAt(current.Count - 1);
            }
        }

        Recurse(0, [], 0);
        return bestCombo ?? [];
    }
}

public record Palette(
    Rgba32 Black,
    Rgba32 RedDark, Rgba32 RedNormal, Rgba32 RedLight,
    Rgba32 GreenDark, Rgba32 GreenNormal, Rgba32 GreenLight,
    Rgba32 BlueDark, Rgba32 BlueNormal, Rgba32 BlueLight
);
