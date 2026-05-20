using Google.Apis.Auth.OAuth2;

namespace PcsRemote.YouTube;

/// <summary>
/// Wraps a <see cref="UserCredential"/> to satisfy <see cref="ICredentialRefresher"/>,
/// keeping the Google Auth dependency out of the interface layer.
/// </summary>
internal sealed class UserCredentialRefresher : ICredentialRefresher
{
    private readonly UserCredential _credential;

    public UserCredentialRefresher(UserCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    /// <inheritdoc/>
    public string? GetRefreshToken() => _credential.Token?.RefreshToken;

    /// <inheritdoc/>
    public async Task RefreshAsync(CancellationToken ct = default) =>
        await _credential.RefreshTokenAsync(ct).ConfigureAwait(false);
}
