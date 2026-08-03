using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed record ScheduledUpdateResult(
    DateTimeOffset CheckedAt,
    IReadOnlyList<AppInfo> Updates);

/// <summary>
/// Runs the in-process six-hour update poll and keeps the persisted task registration
/// aligned with the user's settings. The worker never installs anything automatically.
/// </summary>
public sealed class ScheduledUpdateService : IDisposable
{
    private readonly GitHubService _github;
    private readonly InstallService _installer;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly Action<string> _log;
    private CancellationTokenSource? _workerCancellation;
    private Task? _worker;
    private string? _lastNotificationKey;
    private bool _disposed;

    public event EventHandler<ScheduledUpdateResult>? UpdatesAvailable;

    public ScheduledUpdateService(
        GitHubService github,
        InstallService installer,
        Func<AppSettings> settingsAccessor,
        Action<string> log)
    {
        _github = github;
        _installer = installer;
        _settingsAccessor = settingsAccessor;
        _log = log;
    }

    public void Configure()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _workerCancellation?.Cancel();
        _workerCancellation?.Dispose();
        _workerCancellation = null;
        _worker = null;

        var current = _settingsAccessor();
        if (!current.EnableScheduledUpdateChecks)
        {
            ScheduledTaskRegistrar.Unregister(_log);
            return;
        }

        var hours = Math.Clamp(current.ScheduledUpdateIntervalHours, 1, 24);
        ScheduledTaskRegistrar.Register(hours, _log);
        _workerCancellation = new CancellationTokenSource();
        _worker = RunAsync(TimeSpan.FromHours(hours), _workerCancellation.Token);
    }

    public Task<ScheduledUpdateResult> CheckNowAsync(CancellationToken ct = default)
        => CheckAsync(_settingsAccessor(), _github, _installer, _log, ct);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workerCancellation?.Cancel();
        _workerCancellation?.Dispose();
        _workerCancellation = null;
    }

    public static async Task<ScheduledUpdateResult> CheckAsync(
        AppSettings settings,
        GitHubService github,
        InstallService installer,
        Action<string>? log = null,
        CancellationToken ct = default)
    {
        installer.Reload();
        var progress = log is null ? null : new Progress<string>(log);
        var infos = await github.DiscoverAsync(settings, progress, ct);
        var updates = infos
            .Where(info => installer.Find(info.RepoOwner, info.RepoName) is { } installed
                && VersionCompare.IsRemoteNewer(installed.Version, info.DisplayVersion))
            .OrderBy(info => info.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ScheduledUpdateResult(DateTimeOffset.Now, updates);
    }

    private async Task RunAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            await CheckAndPublishAsync(ct);
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(ct))
                await CheckAndPublishAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _log($"! Background update check stopped: {ex.Message}");
        }
    }

    private async Task CheckAndPublishAsync(CancellationToken ct)
    {
        var result = await CheckNowAsync(ct);
        if (result.Updates.Count == 0)
        {
            _lastNotificationKey = null;
            _log($"Background update check complete: no updates ({result.CheckedAt:HH:mm}).");
            return;
        }

        var key = string.Join(
            "|",
            result.Updates.Select(info => $"{info.RepoOwner}/{info.RepoName}@{info.DisplayVersion}"));
        if (string.Equals(key, _lastNotificationKey, StringComparison.Ordinal))
            return;

        _lastNotificationKey = key;
        _log($"Background update check found {result.Updates.Count} update(s).");
        try { UpdatesAvailable?.Invoke(this, result); }
        catch (Exception ex) { _log($"! Update notification failed: {ex.Message}"); }
    }
}
