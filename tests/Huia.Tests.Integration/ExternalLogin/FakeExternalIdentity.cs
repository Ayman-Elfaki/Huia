namespace Huia.Tests.Integration.ExternalLogin;

/// <summary>The identity <see cref="FakeIdentityProvider"/> reports back for a completed flow.</summary>
public sealed record FakeExternalIdentity(string Subject, string? Email, string? GivenName, string? FamilyName,
    bool EmailVerified, string? Picture = null);
