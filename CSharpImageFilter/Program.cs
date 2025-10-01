using System.Diagnostics;
using CSharpImageFilter;
using SixLabors.ImageSharp.PixelFormats;

class Program
{
    static void Main()
    {
        // 1. Define common parameters.

        var palette = new List<Rgba32>
        {
            Rgba32.ParseHex("#000000"), Rgba32.ParseHex("#3c3c3c"), Rgba32.ParseHex("#787878"),
            Rgba32.ParseHex("#aaaaaa"), Rgba32.ParseHex("#d2d2d2"), Rgba32.ParseHex("#ffffff"),
            Rgba32.ParseHex("#333941"), Rgba32.ParseHex("#6d758d"), Rgba32.ParseHex("#b3b9d1"),
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
        };

        // 3. Create the configuration object, passing your custom palette.
        //    The 'PaletteSize' parameter will now be ignored.
        var config = new DitheringConfig(
            InputPath: "input.png",
            OutputPath: "output.png",
            DownscaleFactor: 3,
            CustomPalette: palette,
            CenterWeight: 1.0f,
            LuminanceBias: 0.0f,
            NeighborhoodSize: 3,
            UseMultiPass: true,
            DarknessThreshold: 35f,
            BlendRange: 10f,
            BilateralFilter: new()
            {
                Enabled = true,
                Radius = 3,
                ColorSigma = 10.0f
            },
            WarmthPenalty: 0.0f,
            GrayscalePenalty: 0.5f
        );

        Stopwatch sw = new();
        sw.Start();
        // 4. Call the processing method. It will now use your predefined colors.
        QuadDitherProcessor.ProcessImage(config);
        sw.Stop();

        Console.WriteLine($"Total done in {sw.ElapsedMilliseconds}ms");
    }
}