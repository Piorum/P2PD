using SixLabors.ImageSharp.PixelFormats;

namespace P2PD.Models;

public record DitheringConfig(
    int DownscaleFactor,
    List<Rgba32> CustomPalette,
    float CenterWeight = 0.9f, // The fix from the previous step!
    float LuminanceBias = 0f, // negative = darker, positive = lighter
    int NeighborhoodSize = 1, // radius on downscaled image used when scoring (0 = single pixel)
    bool UseMultiPass = false, // if true will produce second pass biased darker and blend
    float DarknessThreshold = 30f, // (0-100) Luminance below which dark pass is at full strength.
    float BlendRange = 40f,         // (0-100) How far the effect takes to fade from full to zero.
    BilateralFilterConfig? BilateralFilter = null,
    float WarmthPenalty = 1.0f,
    float GrayscalePenalty = 0.5f
);
