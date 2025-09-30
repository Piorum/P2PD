using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.Numerics;

namespace CSharpImageFilter;

/// <summary>
/// Represents a pre-calculated combination of 4 pixels from the palette.
/// Caching the LabPixels is an optimization to avoid recalculating them.
/// </summary>
public record PixelCombination(Vector3 ColorSum, Rgba32[] Pixels, Lab[] LabPixels);

/// <summary>
/// Configuration for the dithering process.
/// </summary>
/// <param name="PaletteSize">The target number of colors if a palette is generated from the image. This is ignored if a CustomPalette is provided.</param>
/// <param name="LuminanceWeight">Weight for luminance in color matching.</param>
/// <param name="BrightnessBias">Bias to adjust overall image brightness.</param>
/// <param name="Smoothness">Factor for error diffusion smoothness.</param>
/// <param name="CustomPalette">An optional, predefined list of colors to use instead of generating a palette from the image.</param>
public record DitheringConfig(
    int PaletteSize,
    float LuminanceWeight,
    float BrightnessBias,
    float Smoothness,
    List<Rgba32>? CustomPalette = null, // This is the new property
    float SmoothnessPreference = 0.0f 
);

/// <summary>
/// A record to hold a 2x2 grid of colors and its pre-calculated average color.
/// Using a record struct is memory-efficient.
/// </summary>
public readonly record struct ColorQuad(
    Rgba32 AverageColor,
    Rgba32 TopLeft,
    Rgba32 TopRight,
    Rgba32 BottomLeft,
    Rgba32 BottomRight
);

public static class ImageProcessor
{
    public static void ProcessImage(string inputPath, string outputPath, int downscaleFactor, DitheringConfig config)
    {
        List<Rgba32> palette = config.CustomPalette ?? new List<Rgba32>();
        if (palette.Count < 1)
        {
            Console.WriteLine("Error: A palette is required.");
            return;
        }

        Dictionary<Rgba32, ColorQuad> quadsByAverageColor = PrecomputeSimplifiedQuads(palette);
        List<Rgba32> averageColorsPalette = [.. quadsByAverageColor.Keys];

        Console.WriteLine("Optimized lookup structures created.");


        using Image<Rgba32> image = Image.Load<Rgba32>(inputPath);
        Console.WriteLine($"Original image dimensions: {image.Width}x{image.Height}");

        using var downscaledImage = HardDownscale(image, downscaleFactor);

        // --- STEP 3: PROCESS WITH QUADS (Unchanged) ---
        var smallSize = new Size(downscaledImage.Width, downscaledImage.Height);
        var outputSize = new Size(smallSize.Width * 2, smallSize.Height * 2);
        using var outputImage = new Image<Rgba32>(outputSize.Width, outputSize.Height);
        Console.WriteLine($"Creating final output image with dimensions: {outputImage.Width}x{outputImage.Height}");

        Parallel.For(0, downscaledImage.Height, y =>
        {
            for (int x = 0; x < downscaledImage.Width; x++)
            {
                Rgba32 originalPixel = downscaledImage[x, y];
                Rgba32 bestAverageColor = PaletteSelector.FindClosestColor(originalPixel, averageColorsPalette);
                ColorQuad bestQuad = quadsByAverageColor[bestAverageColor];

                outputImage[x * 2, y * 2] = bestQuad.TopLeft;
                outputImage[x * 2 + 1, y * 2] = bestQuad.TopRight;
                outputImage[x * 2, y * 2 + 1] = bestQuad.BottomLeft;
                outputImage[x * 2 + 1, y * 2 + 1] = bestQuad.BottomRight;
            }
        });

        Console.WriteLine("Pixel processing complete.");
        outputImage.Save(outputPath);
        Console.WriteLine($"Image saved successfully to {outputPath} with final dimensions: {outputImage.Width}x{outputImage.Height}");
    }

    // The PrecomputeSimplifiedQuads method remains exactly the same.
    private static Dictionary<Rgba32, ColorQuad> PrecomputeSimplifiedQuads(List<Rgba32> palette)
    {
        var quads = new Dictionary<Rgba32, ColorQuad>();
        foreach (var c1 in palette)
        {
            quads[c1] = new ColorQuad(c1, c1, c1, c1, c1);
        }
        for (int i = 0; i < palette.Count; i++)
        {
            for (int j = i + 1; j < palette.Count; j++)
            {
                var c1 = palette[i];
                var c2 = palette[j];
                int avgR = (c1.R + c2.R) / 2;
                int avgG = (c1.G + c2.G) / 2;
                int avgB = (c1.B + c2.B) / 2;
                var avgColor = new Rgba32((byte)avgR, (byte)avgG, (byte)avgB);
                if (!quads.ContainsKey(avgColor))
                {
                    quads[avgColor] = new ColorQuad(avgColor, c1, c2, c2, c1);
                }
            }
        }
        return quads;
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