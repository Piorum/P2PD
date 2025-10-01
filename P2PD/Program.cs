using System.Diagnostics;
using SixLabors.ImageSharp.PixelFormats;
using P2PD.Models;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp;

namespace P2PD;

class Program
{
    static void Main()
    {
        using var src = Image.Load<Rgba32>("input.png");

        var config = new DitheringConfig(
            PaletteConfig: new GeneratedPalette(Size: 128, RefinementIterations: 1),
            DownscaleFactor: 4,
            LuminanceBias: 0.0f,
            NeighborhoodSize: 3,
            UseMultiPass: true,
            DarknessThreshold: 15f,
            BlendRange: 20f,
            BilateralFilter: new()
            {
                Enabled = true,
                Radius = 4,
                SpatialSigma = 3,
                ColorSigma = 12.0f
            },
            WarmthPenalty: 0.0f,
            GrayscalePenalty: 1.5f
        );

        using var ditheredImage = QuadDitherProcessor.ProcessImage(src.Clone(), config) ?? throw new("Null Returned From Dither Processor");

        WebpEncoder webpEncoder = new()
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 100
        };

        ditheredImage.Save("output.webp", webpEncoder);

        Console.WriteLine($"Done.");
    }
}