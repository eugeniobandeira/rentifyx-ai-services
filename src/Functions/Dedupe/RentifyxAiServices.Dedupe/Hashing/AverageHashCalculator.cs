using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RentifyxAiServices.Dedupe.Hashing;

/// <summary>
/// Classic "average hash" (aHash): shrink to 8x8, grayscale, set each bit to 1 if that pixel is
/// at or above the image's mean brightness. Cheap, no external API call, exact-match only (a
/// crop/rotation/recolor of the same photo produces a different hash) - see ADR-AI-007 for why
/// this was chosen over Rekognition CompareFaces (asset photos rarely contain faces) or Bedrock
/// embeddings (real per-call cost, deferred until fuzzy matching is actually needed).
/// </summary>
public sealed class AverageHashCalculator : IPerceptualHashCalculator
{
    private const int HashSize = 8;

    public string ComputeHash(Stream imageStream)
    {
        using Image<L8> image = Image.Load<L8>(imageStream);

        image.Mutate(ctx => ctx
            .Resize(new ResizeOptions
            {
                Size = new Size(HashSize, HashSize),
                Mode = ResizeMode.Stretch
            })
            .Grayscale());

        Span<byte> pixels = stackalloc byte[HashSize * HashSize];
        int index = 0;

        for (int y = 0; y < HashSize; y++)
        {
            for (int x = 0; x < HashSize; x++)
            {
                pixels[index++] = image[x, y].PackedValue;
            }
        }

        int average = 0;
        foreach (byte pixel in pixels)
        {
            average += pixel;
        }
        average /= pixels.Length;

        ulong hash = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (pixels[i] >= average)
            {
                hash |= 1UL << i;
            }
        }

        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }
}
