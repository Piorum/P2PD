namespace CSharpImageFilter;

class Program
{
    static void Main()
    {
        // 1. Define all parameters in one place.
        string inputPath = "input.png";
        string outputPath = "output.png";
        int downscaleFactor = 3;

        var config = new DitheringConfig(
            PaletteSize: 24,
            LuminanceWeight: 2.5f,
            BrightnessBias: 1.0f,
            Smoothness: 0.3f
        );

        // 2. Call the self-contained processing method.
        ImageProcessor.ProcessImage(inputPath, outputPath, downscaleFactor, config);
    }
}