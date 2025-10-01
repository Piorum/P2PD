// ImageSharp 3.x - 2x2 Quad Pattern Dither (Lab-aware, multi-pattern, neighborhood scoring, optional multi-pass)
// Drop-in processor style utility. Self-contained with simple Lab helpers.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CSharpImageFilter
{
    public record DitheringConfig(
        string InputPath,
        string OutputPath,
        int DownscaleFactor,
        List<Rgba32> CustomPalette,
        float LuminanceBias = 0f, // negative = darker, positive = lighter
        int NeighborhoodSize = 1, // radius on downscaled image used when scoring (0 = single pixel)
        bool UseMultiPass = false // if true will produce second pass biased darker and blend
    );

    // Small Lab helpers. Replace with your optimized versions if present.
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

            Vector3 f = new Vector3(Fxyz(x / 0.95047f), Fxyz(y / 1.00000f), Fxyz(z / 1.08883f));
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

        public static Rgba32 ClampToRgba(float r, float g, float b)
        {
            byte R = (byte)Math.Clamp((int)MathF.Round(r * 255f), 0, 255);
            byte G = (byte)Math.Clamp((int)MathF.Round(g * 255f), 0, 255);
            byte B = (byte)Math.Clamp((int)MathF.Round(b * 255f), 0, 255);
            return new Rgba32(R, G, B);
        }
    }

    // 2x2 pattern.
    public readonly record struct ColorQuad(
        Rgba32 TopLeft,
        Rgba32 TopRight,
        Rgba32 BottomLeft,
        Rgba32 BottomRight,
        Vector3 LabAverage,
        Vector3[] LabPixels
    );

    public static class QuadDitherProcessor
    {
        public static void ProcessImage(DitheringConfig cfg)
        {
            var palette = cfg.CustomPalette ?? throw new ArgumentException("Palette required");
            if (palette.Count == 0) throw new ArgumentException("Palette required");

            // Generate quads with multiple layouts
            var quads = GenerateQuads(palette);

            using var src = Image.Load<Rgba32>(cfg.InputPath);

            // Downscale using simple box average
            using var down = HardDownscale(src, cfg.DownscaleFactor);

            // Optionally apply luminance bias to downscaled image (darken/lighten prior to matching)
            if (Math.Abs(cfg.LuminanceBias) > 1e-6f)
            {
                ApplyLuminanceBias(down, cfg.LuminanceBias);
            }

            var outSize = new Size(down.Width * 2, down.Height * 2);
            using var output = new Image<Rgba32>(outSize.Width, outSize.Height);

            // Precompute Lab array for downscaled image for neighborhood scoring
            var downLab = new Vector3[down.Width, down.Height];
            for (int y = 0; y < down.Height; y++)
                for (int x = 0; x < down.Width; x++)
                    downLab[x, y] = ColorUtils.ToLab(down[x, y]);

            // For each downscaled pixel, score all quads by neighborhood error and choose best
            Parallel.For(0, down.Height, y =>
            {
                for (int x = 0; x < down.Width; x++)
                {
                    // gather neighborhood target labs
                    var neighborhood = GatherNeighborhood(downLab, x, y, cfg.NeighborhoodSize);

                    // find best quad
                    ColorQuad best = default;
                    float bestScore = float.MaxValue;

                    foreach (var q in quads)
                    {
                        float score = ScoreQuad(q, neighborhood);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = q;
                        }
                    }

                    // write quad into output
                    int ox = x * 2;
                    int oy = y * 2;
                    output[ox, oy] = best.TopLeft;
                    output[ox + 1, oy] = best.TopRight;
                    output[ox, oy + 1] = best.BottomLeft;
                    output[ox + 1, oy + 1] = best.BottomRight;
                }
            });

            if (!cfg.UseMultiPass)
            {
                output.Save(cfg.OutputPath);
                return;
            }

            // Multi-pass: create a second pass biased darker and blend by per-pixel error
            using var outputB = new Image<Rgba32>(outSize.Width, outSize.Height);
            // darken downsample by extra bias and re-run selection quickly using same quads
            var downDark = HardCloneAndBias(down, -0.25f);
            var downDarkLab = new Vector3[downDark.Width, downDark.Height];
            for (int y = 0; y < downDark.Height; y++)
                for (int x = 0; x < downDark.Width; x++)
                    downDarkLab[x, y] = ColorUtils.ToLab(downDark[x, y]);

            Parallel.For(0, downDark.Height, y =>
            {
                for (int x = 0; x < downDark.Width; x++)
                {
                    var neighborhood = GatherNeighborhood(downDarkLab, x, y, cfg.NeighborhoodSize);
                    ColorQuad best = default;
                    float bestScore = float.MaxValue;
                    foreach (var q in quads)
                    {
                        float score = ScoreQuad(q, neighborhood);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = q;
                        }
                    }
                    int ox = x * 2;
                    int oy = y * 2;
                    outputB[ox, oy] = best.TopLeft;
                    outputB[ox + 1, oy] = best.TopRight;
                    outputB[ox, oy + 1] = best.BottomLeft;
                    outputB[ox + 1, oy + 1] = best.BottomRight;
                }
            });

            // blend per-pixel choosing the lower Lab error to original downscaled pixel
            using var final = new Image<Rgba32>(outSize.Width, outSize.Height);
            for (int y = 0; y < down.Height; y++)
            {
                for (int x = 0; x < down.Width; x++)
                {
                    int ox = x * 2;
                    int oy = y * 2;
                    // for each subpixel choose output or outputB based on Lab distance to original (no bias)
                    var origNeighborhood = GatherNeighborhood(downLab, x, y, 0); // single target lab

                    // four subpixels
                    for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        var pA = final[ox + sx, oy + sy]; // will be set below
                        var a = output[ox + sx, oy + sy];
                        var b = outputB[ox + sx, oy + sy];

                        var labA = ColorUtils.ToLab(a);
                        var labB = ColorUtils.ToLab(b);
                        var target = downLab[x, y];

                        float eA = ColorUtils.LabDistanceSquared(labA, target);
                        float eB = ColorUtils.LabDistanceSquared(labB, target);

                        final[ox + sx, oy + sy] = eA <= eB ? a : b;
                    }
                }
            }

            final.Save(cfg.OutputPath);
        }

        // Generate a richer set of 2x2 quads: solid, checker, horizontal, vertical
        private static List<ColorQuad> GenerateQuads(List<Rgba32> palette)
        {
            var list = new List<ColorQuad>();
            int n = palette.Count;

            // solids
            foreach (var c in palette)
            {
                var lab = ColorUtils.ToLab(c);
                var labPixels = new Vector3[] { lab, lab, lab, lab };
                list.Add(new ColorQuad(c, c, c, c, lab, labPixels));
            }

            // pairs
            for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var c1 = palette[i];
                var c2 = palette[j];
                var labs = new Vector3[]
                {
                    ColorUtils.ToLab(c1),
                    ColorUtils.ToLab(c2)
                };

                // checker A
                var pA = new Rgba32[] { c1, c2, c2, c1 };
                list.Add(MakeQuad(pA));
                // checker B (swap)
                var pB = new Rgba32[] { c2, c1, c1, c2 };
                list.Add(MakeQuad(pB));
                // horizontal split
                var pH = new Rgba32[] { c1, c1, c2, c2 };
                list.Add(MakeQuad(pH));
                // vertical split
                var pV = new Rgba32[] { c1, c2, c1, c2 };
                list.Add(MakeQuad(pV));
            }

            // optional: remove duplicates by LabAverage key to reduce search space
            var dedup = list.GroupBy(q => (int)MathF.Round(q.LabAverage.X) * 1000000 + (int)MathF.Round(q.LabAverage.Y) * 1000 + (int)MathF.Round(q.LabAverage.Z))
                            .Select(g => g.First())
                            .ToList();
            return dedup;
        }

        private static ColorQuad MakeQuad(Rgba32[] p)
        {
            // p order: TL, TR, BL, BR
            var labs = p.Select(ColorUtils.ToLab).ToArray();
            var avg = new Vector3( (labs[0].X + labs[1].X + labs[2].X + labs[3].X) / 4f,
                                   (labs[0].Y + labs[1].Y + labs[2].Y + labs[3].Y) / 4f,
                                   (labs[0].Z + labs[1].Z + labs[2].Z + labs[3].Z) / 4f);
            return new ColorQuad(p[0], p[1], p[2], p[3], avg, labs);
        }

        // Given a quad and a neighborhood array of target labs, score total squared error
        private static float ScoreQuad(ColorQuad q, Vector3[] neighborhood)
        {
            // neighborhood length should match q.LabPixels length * area factor
            // We will simply compare averaged target to quad pixels weighted by position.
            // Simpler and faster: compute error between quad pixel labs and target lab mean.
            Vector3 targetMean = new Vector3(0,0,0);
            for (int i = 0; i < neighborhood.Length; i++) targetMean += neighborhood[i];
            targetMean /= neighborhood.Length;

            // baseline: error between quad average and target mean
            float score = ColorUtils.LabDistanceSquared(q.LabAverage, targetMean);

            // penalty: variance mismatch (encourage quads whose internal contrast matches target's)
            float targetVar = 0f;
            for (int i = 0; i < neighborhood.Length; i++)
            {
                var d = neighborhood[i] - targetMean;
                targetVar += d.X*d.X + d.Y*d.Y + d.Z*d.Z;
            }
            targetVar /= neighborhood.Length;

            // quad internal variance
            Vector3 quadMean = q.LabAverage;
            float quadVar = 0f;
            for (int i = 0; i < q.LabPixels.Length; i++)
            {
                var d = q.LabPixels[i] - quadMean;
                quadVar += d.X*d.X + d.Y*d.Y + d.Z*d.Z;
            }
            quadVar /= q.LabPixels.Length;

            // weight variance penalty lightly
            float varPenalty = MathF.Abs(quadVar - targetVar) * 0.1f;

            return score + varPenalty;
        }

        private static Vector3[] GatherNeighborhood(Vector3[,] labArray, int cx, int cy, int radius)
        {
            int w = labArray.GetLength(0);
            int h = labArray.GetLength(1);
            var list = new List<Vector3>();
            for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int nx = Math.Clamp(cx + dx, 0, w - 1);
                int ny = Math.Clamp(cy + dy, 0, h - 1);
                list.Add(labArray[nx, ny]);
            }
            return list.ToArray();
        }

        private static Image<Rgba32> HardDownscale(Image<Rgba32> input, int factor)
        {
            int newW = Math.Max(1, input.Width / factor);
            int newH = Math.Max(1, input.Height / factor);
            var result = new Image<Rgba32>(newW, newH);

            for (int y = 0; y < newH; y++)
            for (int x = 0; x < newW; x++)
            {
                float r=0,g=0,b=0,a=0; int count=0;
                for (int dy=0; dy<factor; dy++)
                for (int dx=0; dx<factor; dx++)
                {
                    int sx = x * factor + dx; int sy = y * factor + dy;
                    if (sx >= input.Width || sy >= input.Height) continue;
                    var px = input[sx, sy];
                    r += px.R; g += px.G; b += px.B; a += px.A; count++;
                }
                result[x,y] = new Rgba32((byte)(r/count),(byte)(g/count),(byte)(b/count),(byte)(a/count));
            }
            return result;
        }

        private static void ApplyLuminanceBias(Image<Rgba32> img, float bias)
        {
            // bias -0.5..0.5 where negative darkens
            for (int y=0;y<img.Height;y++)
            for (int x=0;x<img.Width;x++)
            {
                var p = img[x,y];
                // simple linear scale towards black or white
                float sf = 1f + bias;
                float r = Math.Clamp(p.R * sf, 0, 255);
                float g = Math.Clamp(p.G * sf, 0, 255);
                float b = Math.Clamp(p.B * sf, 0, 255);
                img[x,y] = new Rgba32((byte)r,(byte)g,(byte)b,p.A);
            }
        }

        private static Image<Rgba32> HardCloneAndBias(Image<Rgba32> input, float bias)
        {
            var outImg = new Image<Rgba32>(input.Width, input.Height);
            for (int y=0;y<input.Height;y++)
            for (int x=0;x<input.Width;x++)
            {
                var p = input[x,y];
                float sf = 1f + bias;
                float r = Math.Clamp(p.R * sf, 0, 255);
                float g = Math.Clamp(p.G * sf, 0, 255);
                float b = Math.Clamp(p.B * sf, 0, 255);
                outImg[x,y] = new Rgba32((byte)r,(byte)g,(byte)b,p.A);
            }
            return outImg;
        }
    }
}
