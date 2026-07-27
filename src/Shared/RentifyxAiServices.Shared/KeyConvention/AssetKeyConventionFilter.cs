using System.Text.RegularExpressions;

namespace RentifyxAiServices.SharedKernel.KeyConvention;

/// <summary>
/// Matches the `assets/{ownerId}/{assetId}/{filename}` S3 key convention, confirmed cross-repo
/// against rentifyx-asset-registry-api's real S3MediaStorageService (G-001, closed 2026-07-27).
/// Shared between Moderation and Dedupe - both are S3-triggered off the same media bucket and
/// need the identical skip-non-matching-key check, but per ADR-AI-001 they deploy independently,
/// so this lives in Shared rather than one Lambda depending on the other's project.
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
