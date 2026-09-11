using System.Runtime.CompilerServices;

// Lets the two flavor packages register core Huia's internal default service implementations
// (e.g. PendingPhoneSignup, InMemoryOtpRateLimiter, HuiaSmsSender) directly, without exposing them
// on the public API surface.
[assembly: InternalsVisibleTo("Huia.OpenId")]
[assembly: InternalsVisibleTo("Huia.Headless")]
