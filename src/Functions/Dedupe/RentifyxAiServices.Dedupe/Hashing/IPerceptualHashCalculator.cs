namespace RentifyxAiServices.Dedupe.Hashing;

public interface IPerceptualHashCalculator
{
    /// <summary>Computes a hash from the image bytes. Same image content -> same hash; this is an
    /// exact-match average hash, not a fuzzy/near-duplicate comparison (see ADR-AI-007).</summary>
    string ComputeHash(Stream imageStream);
}
