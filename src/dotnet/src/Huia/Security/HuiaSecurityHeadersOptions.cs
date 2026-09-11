namespace Huia.Security;

/// <summary>
/// Tunes the response headers emitted by <c>AddHuiaSecurityHeaders()</c>. The defaults suit the
/// stock account UI (one bundled script, one nonce'd inline style).
/// </summary>
public sealed class HuiaSecurityHeadersOptions
{
    /// <summary>
    /// When <see langword="true"/> (the default) the per-request nonce is added to <c>script-src</c> so
    /// nonce-marked inline scripts run.
    /// </summary>
    public bool AppendNonceToInlineScripts { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, <c>script-src</c> is emitted as <c>'none'</c> — a hard lock-down for
    /// hosts that replace the account UI with a script-free one. Default <see langword="false"/>.
    /// </summary>
    public bool DisableScripts { get; set; }

    /// <summary>Extra sources appended to <c>script-src</c> (for example a CAPTCHA widget origin).</summary>
    public IList<string> AdditionalScriptSrc { get; } = [];

    /// <summary>Extra sources appended to <c>style-src</c>.</summary>
    public IList<string> AdditionalStyleSrc { get; } = [];

    /// <summary>Extra sources appended to <c>connect-src</c>.</summary>
    public IList<string> AdditionalConnectSrc { get; } = [];

    /// <summary>Extra sources appended to <c>img-src</c>.</summary>
    public IList<string> AdditionalImageSrc { get; } = [];

    /// <summary>
    /// Extra sources appended to <c>form-action</c>. The origins of every configured client's redirect,
    /// post-logout and home URIs are added automatically (an interactive sign-in must be able to
    /// redirect the login form POST back to the requesting OAuth client); use this for anything else.
    /// </summary>
    public IList<string> AdditionalFormActionSrc { get; } = [];

    /// <summary>The <c>frame-ancestors</c> directive or X-Frame-Options value. Defaults to <c>'none'</c> / <c>DENY</c>.</summary>
    public string FrameAncestors { get; set; } = "'none'";

    /// <summary>Value of the <c>Strict-Transport-Security</c> header, emitted only over HTTPS.</summary>
    public string StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";

    /// <summary>The <c>Referrer-Policy</c> header value.</summary>
    public string ReferrerPolicy { get; set; } = "no-referrer";

    /// <summary>
    /// The <c>Permissions-Policy</c> header value. The passkey directives are stated explicitly (the
    /// default already permits them for same-origin) so the account UI's WebAuthn ceremonies survive a
    /// stricter host policy.
    /// </summary>
    public string PermissionsPolicy { get; set; } =
        "camera=(), microphone=(), geolocation=(), publickey-credentials-get=(self), publickey-credentials-create=(self)";
}
