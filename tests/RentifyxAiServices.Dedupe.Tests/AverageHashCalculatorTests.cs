using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace RentifyxAiServices.Dedupe.Tests;

public class AverageHashCalculatorTests
{
    private readonly AverageHashCalculator _calculator = new();

    [Fact]
    public void ComputeHash_SameImageTwice_ProducesTheSameHash()
    {
        using MemoryStream imageA = CreateCheckerboardImage();
        using MemoryStream imageB = CreateCheckerboardImage();

        string hashA = _calculator.ComputeHash(imageA);
        string hashB = _calculator.ComputeHash(imageB);

        hashA.Should().Be(hashB);
    }

    [Fact]
    public void ComputeHash_VisuallyDifferentImages_ProducesDifferentHashes()
    {
        using MemoryStream checkerboard = CreateCheckerboardImage();
        using MemoryStream solidWhite = CreateSolidColorImage(Color.White);

        string hash1 = _calculator.ComputeHash(checkerboard);
        string hash2 = _calculator.ComputeHash(solidWhite);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ReturnsA16CharacterHexString()
    {
        using MemoryStream image = CreateSolidColorImage(Color.Gray);

        string hash = _calculator.ComputeHash(image);

        hash.Should().MatchRegex("^[0-9a-f]{16}$");
    }

    private static MemoryStream CreateCheckerboardImage()
    {
        using Image<Rgba32> image = new(64, 64);

        for (int y = 0; y < 64; y++)
        {
            for (int x = 0; x < 64; x++)
            {
                bool isBlack = (x / 8 + y / 8) % 2 == 0;
                image[x, y] = isBlack ? new Rgba32(0, 0, 0) : new Rgba32(255, 255, 255);
            }
        }

        MemoryStream stream = new();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateSolidColorImage(Color color)
    {
        using Image<Rgba32> image = new(64, 64, color);

        MemoryStream stream = new();
        image.SaveAsPng(stream);
        stream.Position = 0;
        return stream;
    }
}
