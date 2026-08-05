using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Server;

namespace Huia.Keys;

/// <summary>
/// Tells <c>IOptionsMonitor&lt;OpenIddictServerOptions&gt;</c> to rebuild its cached value whenever
/// <see cref="KeyManager"/> rotates keys, so a rotation takes effect without an app restart.
/// </summary>
internal sealed class KeyChangeTokenSource(KeyManager keyManager) : IOptionsChangeTokenSource<OpenIddictServerOptions>
{
    public string? Name => Options.DefaultName;

    public IChangeToken GetChangeToken() => keyManager.GetChangeToken();
}