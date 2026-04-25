using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using LocalDesktopStore.Models;
using LocalDesktopStore.Services;

namespace LocalDesktopStore.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly GitHubService _github;
    private readonly InstallService _installer;
    private readonly DispatcherLogSink _logSink;
    private AppSettings _settings;
    private bool _busy;
    private string _statusText = "Ready.";
    private string _searchText = string.Empty;
    private bool _showInstalledOnly;
    private string _githubUserInput = "";
    private string _githubTokenInput = "";

    public ObservableCollection<AppCardViewModel> Apps { get; } = new();
    public ICollectionView AppsView { get; }
    public ObservableCollection<string> LogLines { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveAndRefreshCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
    public ICommand ClearLogCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _github = new GitHubService();
        _installer = new InstallService(_settingsService, _github);
        _settings = _settingsService.Load();
        _logSink = new DispatcherLogSink(LogLines);

        _githubUserInput = _settings.GitHubUser;
        _githubTokenInput = _settings.GitHubToken ?? string.Empty;

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;
        AppsView.SortDescriptions.Add(new SortDescription(nameof(AppCardViewModel.Title), ListSortDirection.Ascending));

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !Busy);
        SaveSettingsCommand = new RelayCommand(_ => { SaveSettings(); });
        SaveAndRefreshCommand = new AsyncRelayCommand(async _ =>
        {
            if (SaveSettings())
                await RefreshAsync();
        }, _ => !Busy);
        OpenInstallDirCommand = new RelayCommand(_ => OpenInstallDir());
        ClearLogCommand = new RelayCommand(_ => LogLines.Clear());

        Log($"LocalDesktopStore v{App.ResourceAssembly.GetName().Version} ready.");
        Log($"Apps install root: {_settingsService.AppsRoot(_settings)}");
        Log($"Run Refresh to discover desktop apps for '{_settings.GitHubUser}'.");
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetField(ref _busy, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(RefreshButtonLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                RefreshAppView();
        }
    }

    public bool ShowInstalledOnly
    {
        get => _showInstalledOnly;
        set
        {
            if (SetField(ref _showInstalledOnly, value))
                RefreshAppView();
        }
    }

    public string GitHubUserInput
    {
        get => _githubUserInput;
        set => SetField(ref _githubUserInput, value);
    }

    public string GitHubTokenInput
    {
        get => _githubTokenInput;
        set => SetField(ref _githubTokenInput, value);
    }

    public bool UseTopicFilter
    {
        get => _settings.UseTopicFilter;
        set
        {
            if (_settings.UseTopicFilter != value)
            {
                _settings.UseTopicFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public string TopicFilter
    {
        get => _settings.TopicFilter;
        set
        {
            if (_settings.TopicFilter != value)
            {
                _settings.TopicFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public bool VerifyHashSidecar
    {
        get => _settings.VerifyHashSidecar;
        set
        {
            if (_settings.VerifyHashSidecar != value)
            {
                _settings.VerifyHashSidecar = value;
                OnPropertyChanged();
            }
        }
    }

    public string InstallRootOverride
    {
        get => _settings.InstallRootOverride ?? string.Empty;
        set
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (_settings.InstallRootOverride != trimmed)
            {
                _settings.InstallRootOverride = trimmed;
                OnPropertyChanged();
            }
        }
    }

    public int InstalledCount => _installer.Installed.Count;
    public int AvailableCount => Apps.Count;
    public int VisibleCount => AppsView.Cast<object>().Count();
    public string RefreshButtonLabel => Busy ? "Refreshing..." : "Refresh";
    public bool ShowEmptyState => !Busy && VisibleCount == 0;
    public string EmptyStateTitle
    {
        get
        {
            if (AvailableCount == 0) return "No apps discovered yet";
            if (ShowInstalledOnly) return "Nothing installed in this view";
            if (!string.IsNullOrWhiteSpace(SearchText)) return "No matching apps";
            return "Nothing to show";
        }
    }
    public string EmptyStateMessage
    {
        get
        {
            if (AvailableCount == 0)
                return "Refresh to scan the configured GitHub account for repos with an MSI / EXE / ZIP release asset.";
            if (ShowInstalledOnly)
                return "Clear the installed-only filter or install an app from the full catalog.";
            if (!string.IsNullOrWhiteSpace(SearchText))
                return "Try a different app name, repository, or description keyword.";
            return "Adjust the filters or refresh the catalog.";
        }
    }

    private bool FilterApp(object obj)
    {
        if (obj is not AppCardViewModel vm) return false;
        if (ShowInstalledOnly && !vm.IsInstalled) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return vm.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || vm.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
            || vm.Repo.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshAsync()
    {
        Busy = true;
        StatusText = "Discovering desktop apps...";
        try
        {
            var logProgress = new Progress<string>(Log);
            var infos = await _github.DiscoverAsync(_settings, logProgress);
            Apps.Clear();
            foreach (var info in infos)
            {
                Apps.Add(new AppCardViewModel(
                    info, _installer, _github, _settingsService, () => _settings, Log, RefreshAfterChange));
            }
            RefreshAppView();
            RefreshMetrics();
            StatusText = $"Found {Apps.Count} app(s) — {InstalledCount} installed.";
            Log(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
            Log($"! {ex}");
        }
        finally
        {
            Busy = false;
        }
    }

    private void RefreshAfterChange()
    {
        RefreshAppView();
        RefreshMetrics();
        CommandManager.InvalidateRequerySuggested();
    }

    private bool SaveSettings()
    {
        var user = GitHubUserInput.Trim();
        var topic = TopicFilter.Trim();
        if (string.IsNullOrWhiteSpace(user))
        {
            StatusText = "Enter a GitHub user or organization before saving.";
            Log("! Settings were not saved: GitHub user / org is required.");
            return false;
        }

        if (UseTopicFilter && string.IsNullOrWhiteSpace(topic))
        {
            StatusText = "Enter a topic filter or turn off topic filtering.";
            Log("! Settings were not saved: topic filter is blank.");
            return false;
        }

        _settings.GitHubUser = user;
        _settings.GitHubToken = string.IsNullOrWhiteSpace(GitHubTokenInput) ? null : GitHubTokenInput.Trim();
        _settings.TopicFilter = topic;
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(TopicFilter));
        Log("Settings saved locally.");
        StatusText = "Settings saved locally.";
        return true;
    }

    private void OpenInstallDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.AppsRoot(_settings)}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"! {ex.Message}"); }
    }

    private void Log(string line) => _logSink.Append(line);

    private void RefreshAppView()
    {
        AppsView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void RefreshMetrics()
    {
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }
}

internal sealed class DispatcherLogSink
{
    private readonly ObservableCollection<string> _sink;
    private const int MaxLines = 500;

    public DispatcherLogSink(ObservableCollection<string> sink) { _sink = sink; }

    public void Append(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            DoAppend(stamped);
        else
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => DoAppend(stamped)));
    }

    private void DoAppend(string line)
    {
        _sink.Add(line);
        while (_sink.Count > MaxLines) _sink.RemoveAt(0);
    }
}
