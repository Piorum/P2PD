using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using P2PD.Models;

namespace P2PD;

public static class QuadDitherProcessor
{
    public static Image<Rgba32>? ProcessImage(Image<Rgba32> src, DitheringConfig cfg)
    {
        var palette = cfg.CustomPalette ?? throw new ArgumentException("Palette required");
        if (palette.Count == 0) throw new ArgumentException("Palette required");

        // Generate quads with multiple layouts
        var quads = GenerateQuads(palette);

        var _quadLut = BuildLut(quads, cfg);
        Console.WriteLine($"Quad LUTs done.");

        var (_paletteLut, _) = BuildPaletteLut(palette, cfg);
        Console.WriteLine($"Palette LUTs done.");

        using var down = HardDownscale(src, cfg.DownscaleFactor);
        Console.WriteLine($"Downscale done.");

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

        var processedDownLab = ApplyBilateralFilter(downLab, cfg.BilateralFilter ?? new());
        Console.WriteLine($"Preproccessing done.");

        // For each downscaled pixel, score all quads by neighborhood error and choose best
        Parallel.For(0, down.Height, y =>
        {
            for (int x = 0; x < down.Width; x++)
            {
                // If the downscaled pixel is transparent, keep it transparent in the output.
                if (down[x, y].A == 0)
                {
                    int ox = x * 2;
                    int oy = y * 2;
                    output[ox, oy] = new Rgba32(0, 0, 0, 0);
                    output[ox + 1, oy] = new Rgba32(0, 0, 0, 0);
                    output[ox, oy + 1] = new Rgba32(0, 0, 0, 0);
                    output[ox + 1, oy + 1] = new Rgba32(0, 0, 0, 0);
                    continue;
                }

                float centerWeight = cfg.CenterWeight; // Good value to start experimenting with

                var neighborhood = GatherNeighborhood(processedDownLab, x, y, cfg.NeighborhoodSize);
                var centerPixelLab = processedDownLab[x, y];

                Vector3 targetMean;

                if (neighborhood.Length <= 1 || centerWeight >= 1.0f)
                {
                    // If there's no neighborhood or weight is 1, just use the center pixel
                    targetMean = centerPixelLab;
                }
                else
                {
                    // Calculate the average of the SURROUNDING pixels (excluding the center)
                    Vector3 neighborSum = Vector3.Zero;
                    for (int i = 0; i < neighborhood.Length; i++)
                    {
                        // A simple way to exclude the center is to check for it,
                        // though this is only truly accurate if neighborhood points are unique.
                        // For this use case, averaging all and then blending is mathematically sound and simpler.
                        neighborSum += neighborhood[i];
                    }

                    Vector3 fullAverage = neighborSum / neighborhood.Length;

                    // Linearly interpolate between the full average and the center pixel's color
                    targetMean = Vector3.Lerp(fullAverage, centerPixelLab, centerWeight);
                }

                // Now use this new 'targetMean' for the LUT lookup
                ColorQuad best = GetBestQuadFromLut(_quadLut, targetMean, quads);

                // write quad into output
                int outX = x * 2;
                int outY = y * 2;
                output[outX, outY] = best.TopLeft;
                output[outX + 1, outY] = best.TopRight;
                output[outX, outY + 1] = best.BottomLeft;
                output[outX + 1, outY + 1] = best.BottomRight;
            }
        });
        Console.WriteLine($"Main pass done.");

        if (!cfg.UseMultiPass)
            return output.Clone();

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
                if (downDark[x, y].A == 0)
                {
                    int ox = x * 2;
                    int oy = y * 2;
                    outputB[ox, oy] = new Rgba32(0, 0, 0, 0);
                    outputB[ox + 1, oy] = new Rgba32(0, 0, 0, 0);
                    outputB[ox, oy + 1] = new Rgba32(0, 0, 0, 0);
                    outputB[ox + 1, oy + 1] = new Rgba32(0, 0, 0, 0);
                    continue;
                }

                float centerWeight = cfg.CenterWeight; // Good value to start experimenting with

                var neighborhood = GatherNeighborhood(downDarkLab, x, y, cfg.NeighborhoodSize);
                var centerPixelLab = downDarkLab[x, y];

                Vector3 targetMean;

                if (neighborhood.Length <= 1 || centerWeight >= 1.0f)
                {
                    // If there's no neighborhood or weight is 1, just use the center pixel
                    targetMean = centerPixelLab;
                }
                else
                {
                    // Calculate the average of the SURROUNDING pixels (excluding the center)
                    Vector3 neighborSum = Vector3.Zero;
                    for (int i = 0; i < neighborhood.Length; i++)
                    {
                        // A simple way to exclude the center is to check for it,
                        // though this is only truly accurate if neighborhood points are unique.
                        // For this use case, averaging all and then blending is mathematically sound and simpler.
                        neighborSum += neighborhood[i];
                    }

                    Vector3 fullAverage = neighborSum / neighborhood.Length;

                    // Linearly interpolate between the full average and the center pixel's color
                    targetMean = Vector3.Lerp(fullAverage, centerPixelLab, centerWeight);
                }

                // Now use this new 'targetMean' for the LUT lookup
                ColorQuad best = GetBestQuadFromLut(_quadLut, targetMean, quads);

                int outX = x * 2;
                int outY = y * 2;
                outputB[outX, outY] = best.TopLeft;
                outputB[outX + 1, outY] = best.TopRight;
                outputB[outX, outY + 1] = best.BottomLeft;
                outputB[outX + 1, outY + 1] = best.BottomRight;
            }
        });
        Console.WriteLine($"Dark pass done.");

        // blend per-pixel choosing the lower Lab error to original downscaled pixel
        using var final = new Image<Rgba32>(outSize.Width, outSize.Height);
        float darknessThreshold = cfg.DarknessThreshold;
        float blendRange = Math.Max(1e-6f, cfg.BlendRange);

        for (int y = 0; y < down.Height; y++)
        {
            for (int x = 0; x < down.Width; x++)
            {
                int ox = x * 2;
                int oy = y * 2;

                if (down[x, y].A == 0) // Handle transparency
                {
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            final[ox + sx, oy + sy] = new Rgba32(0, 0, 0, 0);
                    continue;
                }

                var originalLab = downLab[x, y];
                float luminance = originalLab.X;

                float blendFactor = 0f;
                if (luminance <= darknessThreshold)
                {
                    blendFactor = 1.0f;
                }
                else
                {
                    float distanceIntoRange = luminance - darknessThreshold;
                    blendFactor = 1.0f - Math.Clamp(distanceIntoRange / blendRange, 0.0f, 1.0f);
                }

                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        var colorA = output[ox + sx, oy + sy];
                        var colorB = outputB[ox + sx, oy + sy];

                        if (blendFactor <= 0.01f)
                        {
                            final[ox + sx, oy + sy] = colorA;
                        }
                        else if (blendFactor >= 0.99f)
                        {
                            final[ox + sx, oy + sy] = colorB;
                        }
                        else
                        {
                            // Blend in Lab space for perceptual accuracy
                            var labA = ColorUtils.ToLab(colorA);
                            var labB = ColorUtils.ToLab(colorB);
                            var blendedLab = Vector3.Lerp(labA, labB, blendFactor);

                            // Snap the blended result to the nearest true palette color
                            final[ox + sx, oy + sy] = GetNearestPaletteColor(_paletteLut, blendedLab, palette);
                        }
                    }
            }
        }
        Console.WriteLine($"Blend pass done.");

        return final.Clone();
    }
    
    // A new preprocessing step to apply a bilateral filter on the Lab image data.
    private static Vector3[,] ApplyBilateralFilter(Vector3[,] labImage, BilateralFilterConfig cfg)
    {
        int width = labImage.GetLength(0);
        int height = labImage.GetLength(1);
        var filteredImage = new Vector3[width, height];
        
        // Pre-calculate the denominator for the Gaussian function for speed.
        float spatialFactor = -0.5f / (cfg.SpatialSigma * cfg.SpatialSigma);
        float colorFactor = -0.5f / (cfg.ColorSigma * cfg.ColorSigma);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Vector3 sum = Vector3.Zero;
                float totalWeight = 0.0f;

                for (int dy = -cfg.Radius; dy <= cfg.Radius; dy++)
                {
                    for (int dx = -cfg.Radius; dx <= cfg.Radius; dx++)
                    {
                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                            // 1. Spatial Weight (how far away is the neighbor?)
                            float spatialDistSq = dx * dx + dy * dy;
                            float spatialWeight = MathF.Exp(spatialDistSq * spatialFactor);

                            // 2. Color Weight (how different is the neighbor's color?)
                            float colorDistSq = ColorUtils.LabDistanceSquared(labImage[x, y], labImage[nx, ny]);
                            float colorWeight = MathF.Exp(colorDistSq * colorFactor);
                            
                            // Total weight is the product of both.
                            float weight = spatialWeight * colorWeight;
                            
                            sum += labImage[nx, ny] * weight;
                            totalWeight += weight;
                        }
                    }
                }
                
                filteredImage[x, y] = sum / totalWeight;
            }
        });

        return filteredImage;
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
    
    public static float WarmthPenalty(in Vector3 labA, in Vector3 labB, float strength = 1.0f)
    {
        // Chroma (saturation) of the target color
        float cA = MathF.Sqrt(labA.Y * labA.Y + labA.Z * labA.Z);

        // This logic only applies to near-neutral target colors.
        // A chroma of 25 is a good threshold.
        if (cA > 25.0f)
        {
            return 0.0f;
        }

        // Calculate the difference in the b* (warmth) channel.
        float db = labA.Z - labB.Z;

        // The penalty is the squared difference, amplified by the strength.
        // This adds directly to the (db*db) component of the main distance calculation.
        return db * db * strength;
    }

    public static float GrayscalePenalty(in Vector3 labA, in Vector3 labB, float strength = 0.5f)
    {
        // Chroma (saturation) of the target color
        float cA = MathF.Sqrt(labA.Y * labA.Y + labA.Z * labA.Z);

        // This logic only applies if the target color is very desaturated.
        // A chroma of 10 is a good threshold for what the eye perceives as "gray".
        if (cA > 10.0f)
        {
            return 0.0f;
        }

        // The candidate's chroma (how colorful it is)
        float cB = MathF.Sqrt(labB.Y * labB.Y + labB.Z * labB.Z);

        // The penalty is the candidate's squared chroma. This heavily punishes
        // any colorfulness when a grayscale color is desired.
        return cB * cB * strength;
    }

    // A new method to build the Palette LUT, call this once after loading the palette
    private static (int[,,], List<Vector3>) BuildPaletteLut(List<Rgba32> palette, DitheringConfig cfg, int size = 128)
    {
        var _paletteLut = new int[size, size, size];
        List<Vector3> _paletteLab = [.. palette.Select(ColorUtils.ToLab)];

        const float lMin = 0f, lMax = 100f;
        const float aMin = -120f, aMax = 120f;
        const float bMin = -120f, bMax = 120f;

        Parallel.For(0, size, z => // L dimension
        {
            for (int y = 0; y < size; y++) // a dimension
            {
                for (int x = 0; x < size; x++) // b dimension
                {
                    float l = lMin + (x + 0.5f) * (lMax - lMin) / size;
                    float a = aMin + (y + 0.5f) * (aMax - aMin) / size;
                    float b = bMin + (z + 0.5f) * (bMax - bMin) / size;
                    var targetLab = new Vector3(l, a, b);

                    int bestIndex = -1;
                    float bestScore = float.MaxValue;

                    for (int i = 0; i < _paletteLab.Count; i++)
                    {
                        var candidateLab = _paletteLab[i];
                        float dist = ColorUtils.LabDistanceSquared(candidateLab, targetLab);

                        float penalty = WarmthPenalty(targetLab, candidateLab, strength: cfg.WarmthPenalty);
                        float grayscalePenalty = GrayscalePenalty(targetLab, candidateLab, strength: cfg.GrayscalePenalty);

                        float score = dist + penalty + grayscalePenalty;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestIndex = i;
                        }
                    }
                    _paletteLut[x, y, z] = bestIndex;
                }
            }
        });

        return (_paletteLut, _paletteLab);
    }

    // Helper to get the nearest palette color for any given Lab color
    private static Rgba32 GetNearestPaletteColor(int[,,] _paletteLut, Vector3 labColor, List<Rgba32> palette)
    {
        const int size = 128; // Must match BuildPaletteLut size
        const float lMin = 0f, lMax = 100f;
        const float aMin = -120f, aMax = 120f;
        const float bMin = -120f, bMax = 120f;

        int x = (int)Math.Clamp((labColor.X - lMin) / (lMax - lMin) * size, 0, size - 1);
        int y = (int)Math.Clamp((labColor.Y - aMin) / (aMax - aMin) * size, 0, size - 1);
        int z = (int)Math.Clamp((labColor.Z - bMin) / (bMax - bMin) * size, 0, size - 1);

        int index = _paletteLut[x, y, z];
        return palette[index];
    }

    // A new method to build the LUT
    private static int[,,] BuildLut(List<ColorQuad> quads, DitheringConfig cfg, int size = 128)
    {
        var _quadLut = new int[size, size, size];
        var quadsLab = quads.Select(q => q.LabAverage).ToList();

        // --- STEP 1: Build the Spatial Acceleration Grid ---

        // Define the coarseness of our search grid. 16^3 = 4096 cells. This is a good starting point.
        const int grid_size = 16;
        var grid = new Dictionary<Vector3, List<int>>();

        const float lMin = 0f, lMax = 100f;
        const float aMin = -120f, aMax = 120f;
        const float bMin = -120f, bMax = 120f;

        // 1a: Sort all quads into the coarse grid cells.
        for (int i = 0; i < quadsLab.Count; i++)
        {
            var lab = quadsLab[i];
            var gridPos = new Vector3(
                (int)Math.Clamp((lab.X - lMin) / (lMax - lMin) * grid_size, 0, grid_size - 1),
                (int)Math.Clamp((lab.Y - aMin) / (aMax - aMin) * grid_size, 0, grid_size - 1),
                (int)Math.Clamp((lab.Z - bMin) / (bMax - bMin) * grid_size, 0, grid_size - 1)
            );

            if (!grid.TryGetValue(gridPos, out List<int>? value))
            {
                value = [];
                grid[gridPos] = value;
            }

            value.Add(i);
        }

        // --- STEP 2: Populate the LUT using the Grid ---
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var targetLab = new Vector3(
                        lMin + (x + 0.5f) * (lMax - lMin) / size,
                        aMin + (y + 0.5f) * (aMax - aMin) / size,
                        bMin + (z + 0.5f) * (bMax - bMin) / size
                    );

                    var gridPos = new Vector3(
                        (int)Math.Clamp((targetLab.X - lMin) / (lMax - lMin) * grid_size, 0, grid_size - 1),
                        (int)Math.Clamp((targetLab.Y - aMin) / (aMax - aMin) * grid_size, 0, grid_size - 1),
                        (int)Math.Clamp((targetLab.Z - bMin) / (bMax - bMin) * grid_size, 0, grid_size - 1)
                    );

                    int bestQuadIndex = -1;
                    float bestScore = float.MaxValue;

                    // 2a: Search a 3x3x3 block of grid cells around our target position.
                    // This ensures we find the true nearest neighbor even if it's in an adjacent cell.
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                var searchPos = new Vector3(gridPos.X + dx, gridPos.Y + dy, gridPos.Z + dz);
                                if (grid.TryGetValue(searchPos, out var quadIndices))
                                {
                                    // THIS IS THE KEY: We now loop over a tiny list (e.g., 1-50 quads)
                                    // instead of the full 7,000.
                                    foreach (var index in quadIndices)
                                    {
                                        var candidateLab = quadsLab[index];
                                        float dist = ColorUtils.LabDistanceSquared(candidateLab, targetLab);

                                        float penalty = WarmthPenalty(targetLab, candidateLab, strength: cfg.WarmthPenalty);
                                        float grayscalePenalty = GrayscalePenalty(targetLab, candidateLab, strength: cfg.GrayscalePenalty);

                                        float score = dist + penalty + grayscalePenalty;
                                        if (score < bestScore)
                                        {
                                            bestScore = score;
                                            bestQuadIndex = index;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    _quadLut[x, y, z] = bestQuadIndex;
                }
            }
        });

        return _quadLut;
    }

    // Helper method to look up a quad from the LUT
    private static ColorQuad GetBestQuadFromLut(int[,,] _quadLut, Vector3 targetLab, List<ColorQuad> quads)
    {
        const int size = 128; // Must match the size used in BuildLut
        const float lMin = 0f, lMax = 100f;
        const float aMin = -120f, aMax = 120f;
        const float bMin = -120f, bMax = 120f;

        // Convert Lab color to LUT indices
        int x = (int)Math.Clamp((targetLab.X - lMin) / (lMax - lMin) * size, 0, size - 1);
        int y = (int)Math.Clamp((targetLab.Y - aMin) / (aMax - aMin) * size, 0, size - 1);
        int z = (int)Math.Clamp((targetLab.Z - bMin) / (bMax - bMin) * size, 0, size - 1);

        int quadIndex = _quadLut[x, y, z];
        return quads[quadIndex];
    }

    private static ColorQuad MakeQuad(Rgba32[] p)
    {
        // p order: TL, TR, BL, BR
        var labs = p.Select(ColorUtils.ToLab).ToArray();
        var avg = new Vector3((labs[0].X + labs[1].X + labs[2].X + labs[3].X) / 4f,
                                (labs[0].Y + labs[1].Y + labs[2].Y + labs[3].Y) / 4f,
                                (labs[0].Z + labs[1].Z + labs[2].Z + labs[3].Z) / 4f);
        return new ColorQuad(p[0], p[1], p[2], p[3], avg, labs);
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
        return [.. list];
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
            int transparentCount = 0;

            for (int dy=0; dy<factor; dy++)
            for (int dx=0; dx<factor; dx++)
            {
                int sx = x * factor + dx; int sy = y * factor + dy;
                if (sx >= input.Width || sy >= input.Height) continue;

                var px = input[sx, sy];
                if (px.A == 0)
                {
                    transparentCount++;
                }
                else
                {
                    r += px.R; g += px.G; b += px.B; a += px.A;
                }
                count++;
            }

            if (count == transparentCount)
            {
                result[x, y] = new Rgba32(0, 0, 0, 0);
            }
            else
            {
                int opaqueCount = count - transparentCount;
                if (opaqueCount > 0)
                {
                    result[x, y] = new Rgba32((byte)(r / opaqueCount), (byte)(g / opaqueCount), (byte)(b / opaqueCount), (byte)(a / opaqueCount));
                }
                else
                {
                    result[x, y] = new Rgba32(0, 0, 0, 0);
                }
            }
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
