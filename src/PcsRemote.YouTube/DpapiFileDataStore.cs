using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;

namespace PcsRemote.YouTube;

/// <summary>
/// Custom <see cref="IDataStore"/> that encrypts values with Windows DPAPI
/// (<see cref="ProtectedData"/>) before writing to disk. Each key gets its own
/// file at <c>{FolderPath}/{key}</c>.
/// </summary>
public sealed class DpapiFileDataStore : IDataStore
{
    private readonly string _folderPath;
    private readonly ILogger<DpapiFileDataStore> _logger;

    public DpapiFileDataStore(string folderPath, ILogger<DpapiFileDataStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(logger);

        _folderPath = folderPath;
        _logger = logger;
    }

    /// <summary>
    /// Gets the folder path where encrypted token files are stored.
    /// </summary>
    public string FolderPath => _folderPath;

    /// <inheritdoc/>
    public Task StoreAsync<T>(string key, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        Directory.CreateDirectory(_folderPath);

        var json = JsonSerializer.SerializeToUtf8Bytes(value);
        var encrypted = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);

        var filePath = GetFilePath(key);
        File.WriteAllBytes(filePath, encrypted);

        _logger.LogDebug("Stored encrypted token for key {Key} at {Path}", key, filePath);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<T?> GetAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);
        if (!File.Exists(filePath))
        {
            return Task.FromResult<T?>(default);
        }

        try
        {
            var encrypted = File.ReadAllBytes(filePath);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var value = JsonSerializer.Deserialize<T>(decrypted);
            return Task.FromResult<T?>(value);
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to decrypt token file {Path} — deleting and forcing re-consent",
                filePath);
            TryDeleteFile(filePath);
            return Task.FromResult<T?>(default);
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync<T>(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var filePath = GetFilePath(key);
        TryDeleteFile(filePath);

        _logger.LogDebug("Deleted token for key {Key}", key);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearAsync()
    {
        if (Directory.Exists(_folderPath))
        {
            foreach (var file in Directory.GetFiles(_folderPath))
            {
                TryDeleteFile(file);
            }
        }

        _logger.LogDebug("Cleared all tokens from {Path}", _folderPath);
        return Task.CompletedTask;
    }

    private string GetFilePath(string key)
    {
        var filePath = Path.GetFullPath(Path.Combine(_folderPath, key));
        var folderPath = Path.GetFullPath(_folderPath + Path.DirectorySeparatorChar);

        if (!filePath.StartsWith(folderPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Token key '{key}' resolves outside the token store folder.", nameof(key));
        }

        return filePath;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogDebug("Token file deleted: {Path}", path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Failed to delete token file {Path} — file may be locked", path);
        }
    }
}
