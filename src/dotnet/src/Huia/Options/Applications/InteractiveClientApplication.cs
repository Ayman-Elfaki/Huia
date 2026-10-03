namespace Huia.Options;

/// <summary>
/// Abstract base class for interactive client applications that perform browser redirects.
/// </summary>
public abstract class InteractiveClientApplication : HuiaApplication
{
    /// <summary>Registered redirect URIs.</summary>
    public IList<Uri> RedirectUris { get; } = [];

    /// <summary>Registered post-logout redirect URIs.</summary>
    public IList<Uri> PostLogoutRedirectUris { get; } = [];

    /// <summary>
    /// "Home" URIs for the client. The first entry is used as the sign-out fallback target when the
    /// request carries no registered <c>post_logout_redirect_uri</c>.
    /// </summary>
    public IList<Uri> HomeUris { get; } = [];

    /// <summary>Initializes an interactive application with an optional client identifier.</summary>
    protected InteractiveClientApplication(string clientId = "") : base(clientId)
    {
    }

    /// <inheritdoc />
    public override void Validate(string path, List<string> errors)
    {
        base.Validate(path, errors);

        errors.Require(RedirectUris.Count > 0, HuiaOptionsValidation.Combine(path, nameof(RedirectUris)),
            "must contain at least one URI for an interactive client.");

        foreach (var uri in RedirectUris.Concat(PostLogoutRedirectUris).Concat(HomeUris))
        {
            errors.Require(uri.IsAbsoluteUri, HuiaOptionsValidation.Combine(path, "Uris"),
                $"'{uri}' must be an absolute URI.");
        }
    }
}
