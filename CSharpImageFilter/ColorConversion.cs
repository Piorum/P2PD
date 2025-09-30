using SixLabors.ImageSharp.PixelFormats;

namespace CSharpImageFilter;

/// <summary>
/// Represents a color in the CIELAB color space.
/// </summary>
public readonly record struct Lab(float L, float A, float B);

/// <summary>
/// A self-contained static class for converting between sRGB and CIELAB color spaces.
/// The formulas are based on the standard D65 illuminant for sRGB.
/// </summary>
public static class ColorConversion
{
    // Reference white point for sRGB (D65 illuminant)
    private const float RefX = 95.047f;
    private const float RefY = 100.0f;
    private const float RefZ = 108.883f;

    /// <summary>
    /// Converts a color from the Rgba32 (sRGB) color space to the Lab color space.
    /// </summary>
    public static Lab ToLab(Rgba32 rgb)
    {
        // --- Step 1: Convert sRGB to linear RGB ---
        float r = rgb.R / 255f;
        float g = rgb.G / 255f;
        float b = rgb.B / 255f;

        r = (r > 0.04045f) ? MathF.Pow((r + 0.055f) / 1.055f, 2.4f) : r / 12.92f;
        g = (g > 0.04045f) ? MathF.Pow((g + 0.055f) / 1.055f, 2.4f) : g / 12.92f;
        b = (b > 0.04045f) ? MathF.Pow((b + 0.055f) / 1.055f, 2.4f) : b / 12.92f;

        // --- Step 2: Convert linear RGB to XYZ ---
        float x = r * 0.4124f + g * 0.3576f + b * 0.1805f;
        float y = r * 0.2126f + g * 0.7152f + b * 0.0722f;
        float z = r * 0.0193f + g * 0.1192f + b * 0.9505f;

        x *= 100;
        y *= 100;
        z *= 100;

        // --- Step 3: Convert XYZ to Lab ---
        x /= RefX;
        y /= RefY;
        z /= RefZ;

        x = (x > 0.008856f) ? MathF.Pow(x, 1 / 3f) : (7.787f * x) + (16f / 116f);
        y = (y > 0.008856f) ? MathF.Pow(y, 1 / 3f) : (7.787f * y) + (16f / 116f);
        z = (z > 0.008856f) ? MathF.Pow(z, 1 / 3f) : (7.787f * z) + (16f / 116f);

        float l = (116f * y) - 16f;
        float a = 500f * (x - y);
        float b_lab = 200f * (y - z);

        return new Lab(l, a, b_lab);
    }

    /// <summary>
    /// Converts a color from the Lab color space to the Rgba32 (sRGB) color space.
    /// </summary>
    public static Rgba32 ToRgb(Lab lab)
    {
        // --- Step 1: Convert Lab to XYZ ---
        float y = (lab.L + 16f) / 116f;
        float x = lab.A / 500f + y;
        float z = y - lab.B / 200f;

        const float t0 = 6f / 29f;
        x = (x > t0) ? x * x * x : (x - 16f / 116f) / 7.787f;
        y = (lab.L > 8f) ? y * y * y : lab.L / 903.3f;
        z = (z > t0) ? z * z * z : (z - 16f / 116f) / 7.787f;

        x *= RefX;
        y *= RefY;
        z *= RefZ;

        x /= 100;
        y /= 100;
        z /= 100;

        // --- Step 2: Convert XYZ to linear RGB ---
        float r = x * 3.2406f - y * 1.5372f - z * 0.4986f;
        float g = -x * 0.9689f + y * 1.8758f + z * 0.0415f;
        float b = x * 0.0557f - y * 0.2040f + z * 1.0570f;

        // --- Step 3: Convert linear RGB to sRGB ---
        r = (r > 0.0031308f) ? 1.055f * MathF.Pow(r, 1 / 2.4f) - 0.055f : 12.92f * r;
        g = (g > 0.0031308f) ? 1.055f * MathF.Pow(g, 1 / 2.4f) - 0.055f : 12.92f * g;
        b = (b > 0.0031308f) ? 1.055f * MathF.Pow(b, 1 / 2.4f) - 0.055f : 12.92f * b;

        // Clamp and scale to 0-255
        byte R = (byte)Math.Clamp(r * 255f, 0, 255);
        byte G = (byte)Math.Clamp(g * 255f, 0, 255);
        byte B = (byte)Math.Clamp(b * 255f, 0, 255);

        return new Rgba32(R, G, B, 255);
    }
}