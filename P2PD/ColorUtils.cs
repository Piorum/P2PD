using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace P2PD;

public static class ColorUtils
{
    public static Vector3 ToLab(Rgba32 c)
    {
        // Convert sRGB 0..255 to CIE Lab roughly. Not fully color-managed but sufficient.
        float r = Linearize(c.R / 255f);
        float g = Linearize(c.G / 255f);
        float b = Linearize(c.B / 255f);

        // D65 reference
        float x = r * 0.4124564f + g * 0.3575761f + b * 0.1804375f;
        float y = r * 0.2126729f + g * 0.7151522f + b * 0.0721750f;
        float z = r * 0.0193339f + g * 0.1191920f + b * 0.9503041f;

        Vector3 f = new(Fxyz(x / 0.95047f), Fxyz(y / 1.00000f), Fxyz(z / 1.08883f));
        float L = MathF.Max(0f, 116f * f.Y - 16f);
        float a = 500f * (f.X - f.Y);
        float bLab = 200f * (f.Y - f.Z);
        return new Vector3(L, a, bLab);
    }

    private static float Linearize(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float Fxyz(float t)
        => t > 0.008856f ? MathF.Pow(t, 1f / 3f) : (7.787f * t + 16f / 116f);

    public static float LabDistanceSquared(in Vector3 a, in Vector3 b)
    {
        float dl = a.X - b.X;
        float da = a.Y - b.Y;
        float db = a.Z - b.Z;
        return dl * dl + da * da + db * db;
    }
    
}
