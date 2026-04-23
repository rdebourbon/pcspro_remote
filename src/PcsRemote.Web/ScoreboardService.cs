using System.Security.Cryptography;
using PcsRemote.Core;
using Serilog;

namespace PcsRemote.Web;

/// <summary>
/// Singleton implementation of <see cref="IScoreboardService"/>.
/// Owns the scoreboard image cache, SHA-256 delta detection, and service events
/// consumed by subscribed Blazor components via the circuit-subscription pattern.
/// </summary>
public sealed class ScoreboardService : IScoreboardService
{
    private readonly IPcsProAutomationService _automationService;
    private readonly object _lock = new();

    private byte[]? _currentImage;
    private byte[]? _storedHash;
    private bool _forceRefreshInProgress;

    public ScoreboardService(IPcsProAutomationService automationService)
    {
        _automationService = automationService;
    }

    /// <inheritdoc/>
    public event EventHandler<byte[]> ScoreboardUpdated = delegate { };

    /// <inheritdoc/>
    public event EventHandler RefreshCompleted = delegate { };

    /// <inheritdoc/>
    public byte[]? CurrentImage
    {
        get
        {
            lock (_lock)
                return _currentImage;
        }
    }

    /// <inheritdoc/>
    public async Task CaptureAndBroadcastAsync(CancellationToken ct = default)
    {
        var image = await _automationService.CaptureScoreboardImageAsync(ct);

        if (image is null || image.Length == 0)
        {
            Log.Debug("ScoreboardService: capture returned null or empty bytes — skipping broadcast");
            return;
        }

        var newHash = ComputeHash(image);
        bool changed;

        lock (_lock)
        {
            if (_forceRefreshInProgress)
            {
                Log.Debug("ScoreboardService: force refresh in progress — suppressing poll broadcast");
                return;
            }

            changed = _storedHash is null || !_storedHash.SequenceEqual(newHash);
            if (changed)
            {
                _currentImage = image;
                _storedHash = newHash;
            }
        }

        if (changed)
        {
            Log.Debug("ScoreboardService: new image captured ({Bytes} bytes) — firing ScoreboardUpdated", image.Length);
            ScoreboardUpdated.Invoke(this, image);
        }
        else
        {
            Log.Debug("ScoreboardService: identical hash — broadcast suppressed");
        }
    }

    /// <inheritdoc/>
    public async Task ForceRefreshAsync(CancellationToken ct = default)
    {
        // Step 1: Tell PCS Pro to refresh its scoreboard data from Play-Cricket servers.
        await _automationService.RefreshScoreboardAsync(ct);

        byte[]? previousHash;

        lock (_lock)
        {
            previousHash = _storedHash;
            _storedHash = null;
            _forceRefreshInProgress = true;
        }

        // Step 2: Capture the updated scoreboard image.
        byte[] image;
        try
        {
            var captured = await _automationService.CaptureScoreboardImageAsync(ct);

            if (captured is null || captured.Length == 0)
            {
                lock (_lock)
                {
                    _storedHash = previousHash;
                    _forceRefreshInProgress = false;
                }

                Log.Debug("ScoreboardService: ForceRefreshAsync — capture returned null or empty bytes — skipping broadcast");
                return;
            }

            image = captured;
        }
        catch
        {
            lock (_lock)
            {
                _storedHash = previousHash;
                _forceRefreshInProgress = false;
            }

            throw;
        }

        var newHash = ComputeHash(image);

        lock (_lock)
        {
            _currentImage = image;
            _storedHash = newHash;
            _forceRefreshInProgress = false;
        }

        Log.Debug("ScoreboardService: ForceRefreshAsync complete ({Bytes} bytes) — firing ScoreboardUpdated", image.Length);
        ScoreboardUpdated.Invoke(this, image);
        RefreshCompleted.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void ClearCache()
    {
        lock (_lock)
        {
            _currentImage = null;
            _storedHash = null;
        }
    }

    private static byte[] ComputeHash(byte[] data) => SHA256.HashData(data);
}
