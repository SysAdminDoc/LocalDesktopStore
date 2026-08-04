using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace LocalDesktopStore.Services;

public enum SelfUpdateStatus
{
    Updated,
    NoUpdate,
    NotInstalled,
    Failed
}

public sealed record SelfUpdateResult(SelfUpdateStatus Status, string Message, string? Version = null);

public sealed class VelopackUpdateService
{
    public const string RepositoryUrl = "https://github.com/SysAdminDoc/LocalDesktopStore";

    private readonly Func<UpdateManager> _managerFactory;

    public VelopackUpdateService()
        : this(CreateManager)
    {
    }

    public VelopackUpdateService(Func<UpdateManager> managerFactory)
    {
        _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
    }

    public async Task<SelfUpdateResult> CheckAndApplyAsync(IProgress<int>? progress = null)
    {
        try
        {
            var manager = _managerFactory();
            if (!manager.IsInstalled)
            {
                return new(
                    SelfUpdateStatus.NotInstalled,
                    "Self-update is available after installing the Velopack Setup package.");
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
                return new(SelfUpdateStatus.NoUpdate, "LocalDesktopStore is already up to date.");

            var version = update.TargetFullRelease.Version.ToString();
            Action<int>? progressCallback = progress is null ? null : progress.Report;
            await manager.DownloadUpdatesAsync(update, progressCallback);
            manager.ApplyUpdatesAndRestart(update);
            return new(SelfUpdateStatus.Updated, $"Applying LocalDesktopStore {version} and restarting...", version);
        }
        catch (NotInstalledException)
        {
            return new(
                SelfUpdateStatus.NotInstalled,
                "Self-update is available after installing the Velopack Setup package.");
        }
        catch (Exception ex)
        {
            return new(SelfUpdateStatus.Failed, $"Self-update failed: {ex.Message}");
        }
    }

    private static UpdateManager CreateManager()
    {
        var source = new GithubSource(RepositoryUrl, null, false, null);
        return new UpdateManager(source, null, null);
    }
}
