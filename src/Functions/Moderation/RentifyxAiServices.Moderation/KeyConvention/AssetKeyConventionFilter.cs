using System.Text.RegularExpressions;

namespace RentifyxAiServices.Moderation;

/// <summary>
/// Matches the assumed `assets/{ownerId}/{assetId}/{filename}` S3 key convention.
/// Unconfirmed cross-repo (see E-02 spec's Reality Check) — kept isolated as a single patchable seam.
/// </summary>
public sealed partial class AssetKeyConventionFilter : IKeyConventionFilter
{
    [GeneratedRegex(@"^assets/[0-9a-fA-F-]{36}/[0-9a-fA-F-]{36}/[^/]+$")]
    private static partial Regex AssetKeyPattern();

    public bool Matches(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return AssetKeyPattern().IsMatch(key);
    }
}
