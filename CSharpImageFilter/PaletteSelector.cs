using SixLabors.ImageSharp;
using SixLabors.ImageSharp.ColorSpaces;
using SixLabors.ImageSharp.ColorSpaces.Conversion;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using SixLabors.ImageSharp.Processing;

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
    /// Extracts a globally optimized palette from the image using an Octree Quantizer.
    /// This is the correct, API-compliant way to generate a palette.
    /// </summary>
    public static List<Rgba32> GetOptimizedPalette(Image<Rgba32> image, int paletteSize)
    {
        // 1. Create the top-level quantizer factory.
        var quantizerFactory = new OctreeQuantizer(new QuantizerOptions
        {
            MaxColors = paletteSize
        });

        // 2. Use the factory to create a pixel-specific quantizer instance.
        IQuantizer<Rgba32> quantizer = quantizerFactory.CreatePixelSpecificQuantizer<Rgba32>(image.Configuration);

        // 3. Create a default pixel sampling strategy to scan the entire image.
        //    This was the missing argument from the previous attempt.
        var pixelSamplingStrategy = new DefaultPixelSamplingStrategy();

        // 4. Call BuildPalette. This method returns VOID and modifies the 'quantizer' object in-place.
        //    It populates the quantizer's internal state with colors from the source image.
        quantizer.BuildPalette(pixelSamplingStrategy, image);

        // 5. NOW, retrieve the palette from the populated quantizer.
        //    GetPalette() returns the ReadOnlyMemory<T> containing the final colors.
        ReadOnlyMemory<Rgba32> palette = quantizer.Palette;

        // 6. Convert the ReadOnlyMemory<Rgba32> to a List and return it.
        return palette.Span.ToArray().ToList();
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