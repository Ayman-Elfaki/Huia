using System.Runtime.CompilerServices;
using System.Text.Json;
using OpenIddict.Abstractions;

namespace Huia.Sessions;

/// <summary>
/// Finds the OpenIddict authorizations tied to one session (via the property
/// <see cref="AttachSessionAuthorizationHandler"/> stamps on them) rather than every authorization a subject
/// has anywhere — shared by <c>Huia.Endpoints.Connect.LogoutNotifier</c> (session-scoped sign-out) and the
/// admin <c>/sessions/{id}/revoke</c> endpoint.
/// </summary>
internal static class SessionAuthorizationLookup
{
    public static async IAsyncEnumerable<object> FindBySessionAsync(IOpenIddictAuthorizationManager manager,
        string subject, string sessionId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var authorization in manager.FindBySubjectAsync(subject, cancellationToken).ConfigureAwait(false))
        {
            var properties = await manager.GetPropertiesAsync(authorization, cancellationToken).ConfigureAwait(false);
            if (properties.TryGetValue(SessionAuthorizationProperties.SessionId, out var value)
                && value.ValueKind == JsonValueKind.String && value.GetString() == sessionId)
            {
                yield return authorization;
            }
        }
    }
}