using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LocalDesktopStore.Models;
using LocalDesktopStore.Services;

namespace LocalDesktopStore.ViewModels;

public sealed class AppCardViewModel : ViewModelBase
{
    private readonly InstallService _installer;
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly Func<AppSettings> _settingsAccessor;
    private readonly Action<string> _log;
    private readonly Action _refreshParent;

    private bool _busy;
    private string? _busyMessage;
    private string? _errorMessage;
    private InstalledApp? _installed;
    private BitmapImage? _icon;

    public AppInfo Info { get; }

    public AppCardViewModel(
        AppInfo info,
        InstallService installer,
        GitHubService github,
        SettingsService settings,
        Func<AppSettings> settingsAccessor,
        Action<string> log,
        Action refreshParent)
    {
        Info = info;
        _installer = installer;
        _github = github;
        _settings = settings;
        _settingsAccessor = settingsAccessor;
        _log = log;
        _refreshParent = refreshParent;
        _installed = installer.Find(info.RepoOwner, info.RepoName);

        InstallCommand = new AsyncRelayCommand(_ => RunInstallAsync(CancellationToken.None), _ => CanInstall);
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, _ => IsInstalled && !Busy);
        RunCommand = new RelayCommand(_ => Run(), _ => CanRun);
        OpenRepoCommand = new RelayCommand(_ => OpenUrl(Info.RepoUrl));
        OpenInstallDirCommand = new RelayCommand(_ => OpenDir(), _ => CanOpenDir);
        _ = LoadIconAsync();
    }

    public string Title => Info.DisplayName;
    public string Version => Info.DisplayVersion;
    public string Description => Info.DisplayDescription;
    public string RepoUrl => Info.RepoUrl;
    public string Repo => $"{Info.RepoOwner}/{Info.RepoName}";
    public string KindLabel => Info.Kind.DisplayName();
    public string AssetSummary => Info.AssetUrl != null
        ? $"{Info.AssetName} • {FormatSize(Info.AssetSizeBytes)}"
        : "Add an MSI / EXE / ZIP release asset to enable install.";
    public string ReleaseSummary => Info.PublishedAt.HasValue
        ? $"Released {Info.PublishedAt.Value.LocalDateTime:MMM d, yyyy}"
        : "Release date unavailable";
    public string Stars => Info.Stars > 0 ? $"★ {Info.Stars}" : string.Empty;
    public bool HasAsset => !string.IsNullOrEmpty(Info.AssetUrl);
    public bool IsInstalled => _installed != null;
    public bool IsUpdateAvailable => IsInstalled
        && VersionCompare.IsRemoteNewer(_installed!.Version, Info.DisplayVersion);
    public bool CanInstall => HasAsset && !Busy;
    public bool CanRun => IsInstalled && !Busy;
    public bool CanOpenDir => IsInstalled && !Busy
        && (_installed?.PortableRoot != null || _installed?.InstallLocation != null);
    public string InstallButtonLabel
    {
        get
        {
            if (!IsInstalled) return HasAsset ? "Install" : "Unavailable";
            return VersionCompare.Compare(_installed!.Version, Info.DisplayVersion) switch
            {
                VersionCompare.Result.Equal => "Reinstall",
                VersionCompare.Result.RemoteOlder => "Reinstall",
                _ => $"Update to {Info.DisplayVersion}"
            };
        }
    }
    public string StatusBadge => IsInstalled
        ? (IsUpdateAvailable ? "Update available" : "Installed")
        : (HasAsset ? "Ready to install" : "Release needed");
    public string InstalledDetail => IsInstalled
        ? $"Local v{_installed!.Version} • {_installed.Kind.DisplayName()}"
        : "Not installed locally";

    public BitmapImage? Icon
    {
        get => _icon;
        private set => SetField(ref _icon, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetField(ref _busy, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanOpenDir));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand OpenInstallDirCommand { get; }

    public async Task RunInstallAsync(CancellationToken ct)
    {
        if (!HasAsset) return;
        Busy = true;
        ErrorMessage = null;
        try
        {
            BusyMessage = "Preparing download...";
            var bytesProgress = new Progress<long>(b =>
            {
                if (Info.AssetSizeBytes > 0)
                {
                    var pct = (int)Math.Min(100, b * 100L / Info.AssetSizeBytes);
                    BusyMessage = $"Downloading {pct}%";
                }
                else
                {
                    BusyMessage = $"Downloading {FormatSize(b)}";
                }
            });
            var logProgress = new Progress<string>(_log);
            _installed = await _installer.InstallAsync(Info, _settingsAccessor(), logProgress, bytesProgress, ct);
            BusyMessage = "Installed";
            RaiseAllChanged();
            _refreshParent();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log($"! Install failed for {Repo}: {ex.Message}");
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            Busy = false;
            BusyMessage = null;
        }
    }

    private async Task UninstallAsync(object? _)
    {
        if (_installed is null) return;
        var confirm = MessageBox.Show(
            $"Uninstall {Title} v{_installed.Version}?",
            "Uninstall app",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        Busy = true;
        ErrorMessage = null;
        try
        {
            BusyMessage = "Uninstalling...";
            var logProgress = new Progress<string>(_log);
            await _installer.UninstallAsync(_installed, logProgress);
            _installed = null;
            RaiseAllChanged();
            _refreshParent();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _log($"! Uninstall failed for {Repo}: {ex.Message}");
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            Busy = false;
            BusyMessage = null;
        }
    }

    private void Run()
    {
        if (_installed is null) return;
        var logProgress = new Progress<string>(_log);
        _installer.TryRun(_installed, logProgress);
    }

    private void OpenDir()
    {
        var path = _installed?.PortableRoot ?? _installed?.InstallLocation;
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _log($"! open dir failed: {ex.Message}"); }
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log($"! open url failed: {ex.Message}"); }
    }

    private async Task LoadIconAsync()
    {
        if (Info.IconCandidates.Count == 0) return;
        try
        {
            var cacheKey = $"{Info.RepoOwner}_{Info.RepoName}.png";
            var cachePath = Path.Combine(_settings.IconCacheDir, cacheKey);
            byte[]? bytes = null;
            if (File.Exists(cachePath))
            {
                bytes = await File.ReadAllBytesAsync(cachePath);
            }
            else
            {
                foreach (var url in Info.IconCandidates)
                {
                    var attempt = await _github.TryDownloadBytesAsync(url);
                    if (attempt != null && attempt.Length > 0)
                    {
                        bytes = attempt;
                        break;
                    }
                }
                if (bytes != null) await File.WriteAllBytesAsync(cachePath, bytes);
            }
            if (bytes == null || bytes.Length == 0) return;

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Icon = bmp;
        }
        catch { /* ignore — fall back to placeholder */ }
    }

    public void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(CanRun));
        OnPropertyChanged(nameof(InstallButtonLabel));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(HasAsset));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(CanOpenDir));
        OnPropertyChanged(nameof(InstalledDetail));
        OnPropertyChanged(nameof(HasError));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] u = ["B", "KB", "MB", "GB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
