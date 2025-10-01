using System.Numerics;
using SixLabors.ImageSharp.PixelFormats;

namespace P2PD.Models;

public readonly record struct ColorQuad(
    Rgba32 TopLeft,
    Rgba32 TopRight,
    Rgba32 BottomLeft,
    Rgba32 BottomRight,
    Vector3 LabAverage,
    Vector3[] LabPixels
);
