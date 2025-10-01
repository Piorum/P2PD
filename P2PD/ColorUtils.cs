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

    public static float LabDistanceSquared(in Vector3 a, in Vector3 b)
    {
        float dl = a.X - b.X;
        float da = a.Y - b.Y;
        float db = a.Z - b.Z;
        return dl * dl + da * da + db * db;
    }

    public static Rgba32 ToRgba32(Vector3 lab)
    {
        float l = lab.X;
        float a = lab.Y;
        float b_lab = lab.Z;

        float fy = (l + 16f) / 116f;
        float fx = a / 500f + fy;
        float fz = fy - b_lab / 200f;

        float x_xyz = Fxyz_inv(fx) * 0.95047f;
        float y_xyz = Fxyz_inv(fy) * 1.00000f;
        float z_xyz = Fxyz_inv(fz) * 1.08883f;

        // XYZ to linear sRGB
        float r_linear =  3.2404542f * x_xyz - 1.5371385f * y_xyz - 0.4985314f * z_xyz;
        float g_linear = -0.9692660f * x_xyz + 1.8760108f * y_xyz + 0.0415560f * z_xyz;
        float b_linear =  0.0556434f * x_xyz - 0.2040259f * y_xyz + 1.0572252f * z_xyz;

        // Linear to sRGB
        byte r = (byte)Math.Clamp(Delinearize(r_linear) * 255f, 0, 255);
        byte g = (byte)Math.Clamp(Delinearize(g_linear) * 255f, 0, 255);
        byte b = (byte)Math.Clamp(Delinearize(b_linear) * 255f, 0, 255);

        return new Rgba32(r, g, b);
    }
    
    private static float Linearize(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static float Fxyz(float t)
        => t > 0.008856f ? MathF.Pow(t, 1f / 3f) : (7.787f * t + 16f / 116f);

    private static float Delinearize(float c)
        => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;

    private static float Fxyz_inv(float t)
        => t > 0.20689655f ? t * t * t : (t - 16f / 116f) / 7.787f;
    
}
