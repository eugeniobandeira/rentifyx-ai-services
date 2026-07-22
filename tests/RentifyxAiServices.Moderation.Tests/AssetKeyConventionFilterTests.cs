using FluentAssertions;
using Xunit;

namespace RentifyxAiServices.Moderation.Tests;

public class AssetKeyConventionFilterTests
{
    private readonly AssetKeyConventionFilter _filter = new();

    [Fact]
    public void Matches_WellFormedAssetKey_ReturnsTrue()
    {
        bool result = _filter.Matches("assets/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg");

        result.Should().BeTrue();
    }

    [Fact]
    public void Matches_MissingSegment_ReturnsFalse()
    {
        bool result = _filter.Matches("assets/11111111-1111-1111-1111-111111111111/photo.jpg");

        result.Should().BeFalse();
    }

    [Fact]
    public void Matches_ThumbnailDerivativeOutsidePrefix_ReturnsFalse()
    {
        bool result = _filter.Matches("thumbnails/11111111-1111-1111-1111-111111111111/22222222-2222-2222-2222-222222222222/photo.jpg");

        result.Should().BeFalse();
    }

    [Fact]
    public void Matches_EmptyString_ReturnsFalseWithoutThrowing()
    {
        bool result = _filter.Matches(string.Empty);

        result.Should().BeFalse();
    }
}
