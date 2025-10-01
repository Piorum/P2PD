namespace P2PD.Models;

public record BilateralFilterConfig(
    bool Enabled = true,
    int Radius = 2, //how many pixels effect calculation
    float SpatialSigma = 2.0f, //how highly far pixels effect calculation
    float ColorSigma = 10.0f //threshold for similar colors
);
