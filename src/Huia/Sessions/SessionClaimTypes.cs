using Huia.Common;

namespace Huia.Sessions;

/// <summary>
/// The claim type a session id is carried as — on the Identity cookie (added right after sign-in, see
/// <see cref="Huia.Identity.HuiaSignInManager"/>), on the identity token/logout_token (see
/// <see cref="ClaimsUtils"/>/<c>Huia.Endpoints.Connect.LogoutNotifier</c>), and internally on an OpenIddict
/// authorization's Properties bag (see <c>Huia.Sessions.SessionAuthorizationProperties</c>). One constant
/// shared everywhere so the literal only appears once.
/// </summary>
internal static class SessionClaimTypes
{
    public const string Sid = "sid";
}