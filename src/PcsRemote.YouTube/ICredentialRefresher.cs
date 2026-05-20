namespace PcsRemote.YouTube;

/// <summary>
/// Abstracts the Google <c>UserCredential</c> token-refresh operation to allow
/// injection of test doubles without requiring a real OAuth flow.
/// </summary>
public interface ICredentialRefresher
{
    /// <summary>
    /// Returns the current refresh token value, or <see langword="null"/> when
    /// no token is present.
    /// </summary>
    string? GetRefreshToken();

    /// <summary>
    /// Calls the underlying credential's token-refresh endpoint.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
