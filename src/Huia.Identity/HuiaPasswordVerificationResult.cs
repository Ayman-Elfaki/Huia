namespace Huia.Identity;

/// <summary>The outcome of <see cref="IHuiaPasswordHasher.VerifyHashedPassword"/>.</summary>
public enum HuiaPasswordVerificationResult
{
    /// <summary>The password didn't match.</summary>
    Failed,

    /// <summary>The password matched, and the stored hash is up to date.</summary>
    Success,

    /// <summary>The password matched, but the stored hash was produced with outdated parameters and should be
    /// rehashed and re-persisted.</summary>
    SuccessRehashNeeded,
}
