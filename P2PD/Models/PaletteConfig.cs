using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Generic;

namespace P2PD.Models;

public abstract record PaletteConfig;

public record PresetPalette(List<Rgba32> Palette) : PaletteConfig;

public record GeneratedPalette(int Size = 64, int RefinementIterations = 5) : PaletteConfig;