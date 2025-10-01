using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace P2PD;

public static class PaletteGenerator
{
    public static List<Rgba32> Generate(Image<Rgba32> image, int colorCount, int maxIterations = 10, int sampleSize = 20000)
    {
        var pixels = new List<Rgba32>();
        var random = new Random();

        // Sample pixels from the image
        if (image.Width * image.Height <= sampleSize)
        {
            for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                if (image[x, y].A > 128) pixels.Add(image[x, y]);
            }
        }
        else
        {
            for (int i = 0; i < sampleSize; i++)
            {
                int x = random.Next(image.Width);
                int y = random.Next(image.Height);
                if (image[x, y].A > 128) pixels.Add(image[x, y]);
            }
        }

        if (pixels.Count == 0) return [];

        // If we have fewer unique pixels than requested colors, just return the unique pixels
        var uniquePixels = pixels.Distinct().ToList();
        if (uniquePixels.Count <= colorCount)
        {
            return uniquePixels;
        }

        var labPixels = uniquePixels.Select(ColorUtils.ToLab).ToArray();
        
        // Initialize centroids by picking random pixels
        var centroids = new Vector3[colorCount];
        var initialCentroidIndices = new HashSet<int>();
        while (initialCentroidIndices.Count < colorCount)
        {
            initialCentroidIndices.Add(random.Next(labPixels.Length));
        }

        int centroidIndex = 0;
        foreach (var index in initialCentroidIndices)
        {
            centroids[centroidIndex++] = labPixels[index];
        }

        var clusters = new int[labPixels.Length];

        // K-means iterations
        for (int iter = 0; iter < maxIterations; iter++)
        {
            // Assign pixels to clusters
            Parallel.For(0, labPixels.Length, i =>
            {
                float minDist = float.MaxValue;
                int bestCluster = 0;
                for (int j = 0; j < colorCount; j++)
                {
                    float dist = ColorUtils.LabDistanceSquared(labPixels[i], centroids[j]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestCluster = j;
                    }
                }
                clusters[i] = bestCluster;
            });

            // Update centroids
            var newCentroids = new Vector3[colorCount];
            var clusterCounts = new int[colorCount];
            for (int i = 0; i < labPixels.Length; i++)
            {
                newCentroids[clusters[i]] += labPixels[i];
                clusterCounts[clusters[i]]++;
            }

            bool changed = false;
            for (int i = 0; i < colorCount; i++)
            {
                if (clusterCounts[i] > 0)
                {
                    var newCentroid = newCentroids[i] / clusterCounts[i];
                    if (ColorUtils.LabDistanceSquared(newCentroid, centroids[i]) > 1e-6f)
                    {
                        centroids[i] = newCentroid;
                        changed = true;
                    }
                }
                else
                {
                    // Re-initialize empty cluster
                    centroids[i] = labPixels[random.Next(labPixels.Length)];
                    changed = true;
                }
            }

            if (!changed && iter > 0) break; // Converged
        }

        // Create palette by finding the original pixel color closest to each centroid
        var palette = new HashSet<Rgba32>();
        for (int i = 0; i < colorCount; i++)
        {
            float minDist = float.MaxValue;
            int bestPixelIndex = -1;

            for (int j = 0; j < labPixels.Length; j++)
            {
                if (clusters[j] == i)
                {
                    float dist = ColorUtils.LabDistanceSquared(labPixels[j], centroids[i]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestPixelIndex = j;
                    }
                }
            }

            if (bestPixelIndex != -1)
            {
                palette.Add(uniquePixels[bestPixelIndex]);
            }
        }

        return [.. palette];
    }

    public static List<Rgba32> Refine(Vector3[,] labImage, List<Rgba32> initialPalette, int maxIterations = 5)
    {
        int width = labImage.GetLength(0);
        int height = labImage.GetLength(1);
        var labPixels = new Vector3[width * height];
        int pIndex = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                labPixels[pIndex++] = labImage[x, y];
            }
        }

        var centroids = initialPalette.Select(ColorUtils.ToLab).ToList();
        int colorCount = centroids.Count;

        if (colorCount == 0) return new List<Rgba32>();

        var clusters = new int[labPixels.Length];
        var random = new Random();

        for (int iter = 0; iter < maxIterations; iter++)
        {
            Parallel.For(0, labPixels.Length, i =>
            {
                float minDist = float.MaxValue;
                int bestCluster = 0;
                for (int j = 0; j < colorCount; j++)
                {
                    float dist = ColorUtils.LabDistanceSquared(labPixels[i], centroids[j]);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        bestCluster = j;
                    }
                }
                clusters[i] = bestCluster;
            });

            var newCentroids = new Vector3[colorCount];
            var clusterCounts = new int[colorCount];
            for (int i = 0; i < labPixels.Length; i++)
            {
                newCentroids[clusters[i]] += labPixels[i];
                clusterCounts[clusters[i]]++;
            }

            bool changed = false;
            for (int i = 0; i < colorCount; i++)
            {
                if (clusterCounts[i] > 0)
                {
                    var newCentroid = newCentroids[i] / clusterCounts[i];
                    if (ColorUtils.LabDistanceSquared(newCentroid, centroids[i]) > 1e-6f)
                    {
                        centroids[i] = newCentroid;
                        changed = true;
                    }
                }
                else
                {
                    centroids[i] = labPixels[random.Next(labPixels.Length)];
                    changed = true;
                }
            }

            if (!changed && iter > 0) break;
        }

        return centroids.Select(ColorUtils.ToRgba32).Distinct().ToList();
    }
}