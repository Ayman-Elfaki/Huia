using System.Text.Json;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace Huia.Sessions;

/// <summary>
/// Tags the OpenIddict authorization OpenIddict's own <c>AttachAuthorization</c> handler just created (or
/// reused) for this sign-in with the <c>sid</c> claim from the same principal, so a later logout can find
/// exactly the authorizations tied to one session (<see cref="SessionAuthorizationLookup"/>) instead of every
/// authorization the subject has anywhere.
/// </summary>
/// <remarks>
/// Registered against <c>ProcessSignInContext</c> — the same event <c>AttachAuthorization</c> handles — at
/// an order comfortably after OpenIddict's built-ins that resolve
/// <see cref="OpenIddictExtensions.GetAuthorizationId(System.Security.Claims.ClaimsPrincipal)"/> on the
/// principal (<c>AttachAuthorization</c> itself, then various token-generation handlers, topping out
/// around 112,000), but strictly before the terminal <c>Apply*Response</c> handlers (order 500,000) that
/// actually write the HTTP response: <c>OpenIddictServerDispatcher</c> stops dispatching to any remaining
/// handler for a context the instant one sets <c>IsRequestHandled</c>, which those response-writing
/// handlers do — a handler registered with <c>SetOrder(int.MaxValue)</c>, sorted after them, would silently
/// never run at all. A missing <c>sid</c> or authorization id (client_credentials tokens carry neither; a
/// token minted before this feature shipped carries no <c>sid</c>) is a silent no-op, not an error.
/// </remarks>
internal sealed class AttachSessionAuthorizationHandler(IOpenIddictAuthorizationManager manager)
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        var sessionId = context.Principal?.GetClaim(SessionClaimTypes.Sid);
        var authorizationId = context.Principal?.GetAuthorizationId();

        if (sessionId is null || authorizationId is null) return;

        var authorization = await manager.FindByIdAsync(authorizationId, context.CancellationToken)
            .ConfigureAwait(false);

        if (authorization is null) return;

        var properties = await manager.GetPropertiesAsync(authorization, context.CancellationToken)
            .ConfigureAwait(false);

        if (properties.TryGetValue(SessionAuthorizationProperties.SessionId, out var existing)
            && existing.ValueKind == JsonValueKind.String && existing.GetString() == sessionId)
        {
            return;
        }

        var descriptor = new OpenIddictAuthorizationDescriptor();
        await manager.PopulateAsync(descriptor, authorization, context.CancellationToken).ConfigureAwait(false);
        descriptor.Properties[SessionAuthorizationProperties.SessionId] = JsonSerializer.SerializeToElement(sessionId);
        await manager.UpdateAsync(authorization, descriptor, context.CancellationToken).ConfigureAwait(false);
    }
}