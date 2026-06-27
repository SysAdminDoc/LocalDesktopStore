using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
    private string _extraOwnerInput = "";
    private string _hiddenRepoInput = "";

    public ObservableCollection<AppCardViewModel> Apps { get; } = new();
    public ICollectionView AppsView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<string> ExtraOwners { get; } = new();
    public ObservableCollection<string> HiddenRepos { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveAndRefreshCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand AddExtraOwnerCommand { get; }
    public ICommand RemoveExtraOwnerCommand { get; }
    public ICommand AddHiddenRepoCommand { get; }
    public ICommand RemoveHiddenRepoCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _github = new GitHubService();
        _installer = new InstallService(_settingsService, _github);
        _settings = _settingsService.Load();
        _logSink = new DispatcherLogSink(LogLines);

        _githubUserInput = _settings.GitHubUser;
        _githubTokenInput = _settings.GitHubToken ?? string.Empty;
        ReplaceCollection(ExtraOwners, NormalizeOwners(_settings.ExtraOwners, _settings.GitHubUser));
        ReplaceCollection(HiddenRepos, NormalizeRepos(_settings.HiddenRepos));
        ExtraOwners.CollectionChanged += OnSettingsListChanged;
        HiddenRepos.CollectionChanged += OnSettingsListChanged;

        AppsView = CollectionViewSource.GetDefaultView(Apps);
        AppsView.Filter = FilterApp;
        AppsView.SortDescriptions.Add(new SortDescription(nameof(AppCardViewModel.Title), ListSortDirection.Ascending));

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !Busy);
        UpdateAllCommand = new AsyncRelayCommand(_ => UpdateAllAsync(), _ => !Busy && OutdatedCount > 0);
        SaveSettingsCommand = new RelayCommand(_ => { SaveSettings(); });
        SaveAndRefreshCommand = new AsyncRelayCommand(async _ =>
        {
            if (SaveSettings())
                await RefreshAsync();
        }, _ => !Busy);
        OpenInstallDirCommand = new RelayCommand(_ => OpenInstallDir());
        ClearLogCommand = new RelayCommand(_ => LogLines.Clear());
        AddExtraOwnerCommand = new RelayCommand(_ => AddExtraOwner());
        RemoveExtraOwnerCommand = new RelayCommand(RemoveExtraOwner);
        AddHiddenRepoCommand = new RelayCommand(_ => AddHiddenRepo());
        RemoveHiddenRepoCommand = new RelayCommand(RemoveHiddenRepo);

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

    public string ExtraOwnerInput
    {
        get => _extraOwnerInput;
        set
        {
            if (SetField(ref _extraOwnerInput, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string HiddenRepoInput
    {
        get => _hiddenRepoInput;
        set
        {
            if (SetField(ref _hiddenRepoInput, value))
                CommandManager.InvalidateRequerySuggested();
        }
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
    public int OutdatedCount => Apps.Count(a => a.IsUpdateAvailable);
    public bool HasExtraOwners => ExtraOwners.Count > 0;
    public bool HasHiddenRepos => HiddenRepos.Count > 0;
    public bool HasOutdated => OutdatedCount > 0;
    public string UpdateAllButtonLabel => OutdatedCount > 0
        ? $"Update all ({OutdatedCount})"
        : "Update all";
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
        if (vm.IsHidden) return false;
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
            // Re-read installed.json so cards built below see the freshest install state
            // (e.g. an out-of-band install that ran while the app was open).
            _installer.Reload();
            Apps.Clear();
            foreach (var info in infos)
            {
                Apps.Add(new AppCardViewModel(
                    info, _installer, _github, _settingsService, () => _settings, Log, RefreshAfterChange));
            }
            ApplyHiddenRepoState();
            RefreshAppView();
            RefreshMetrics();
            var outdated = OutdatedCount;
            StatusText = outdated > 0
                ? $"Found {Apps.Count} app(s) — {InstalledCount} installed, {outdated} update(s) available."
                : $"Found {Apps.Count} app(s) — {InstalledCount} installed.";
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

    private async Task UpdateAllAsync()
    {
        var queue = Apps.Where(a => a.IsUpdateAvailable && a.HasAsset).ToList();
        if (queue.Count == 0)
        {
            StatusText = "Nothing to update.";
            return;
        }

        Busy = true;
        var ok = 0;
        var failed = 0;
        try
        {
            for (var idx = 0; idx < queue.Count; idx++)
            {
                var card = queue[idx];
                StatusText = $"Updating {card.Title} ({idx + 1}/{queue.Count})...";
                Log($"Update {idx + 1}/{queue.Count}: {card.Repo} -> {card.Info.DisplayVersion}");
                try
                {
                    await card.RunInstallAsync(CancellationToken.None);
                    if (card.HasError) failed++; else ok++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log($"! Update failed for {card.Repo}: {ex.Message}");
                }
            }
            StatusText = failed == 0
                ? $"Updated {ok} app(s)."
                : $"Updated {ok} app(s); {failed} failed — see activity log.";
            Log(StatusText);
        }
        finally
        {
            Busy = false;
            RefreshMetrics();
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
        _settings.ExtraOwners = NormalizeOwners(ExtraOwners, user).ToList();
        _settings.HiddenRepos = NormalizeRepos(HiddenRepos).ToList();
        ReplaceCollection(ExtraOwners, _settings.ExtraOwners);
        ReplaceCollection(HiddenRepos, _settings.HiddenRepos);
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(TopicFilter));
        ApplyHiddenRepoState();
        RefreshAppView();
        Log("Settings saved locally.");
        StatusText = "Settings saved locally.";
        return true;
    }

    private void AddExtraOwner()
    {
        var owner = NormalizeOwner(ExtraOwnerInput);
        if (owner is null)
        {
            StatusText = "Enter a GitHub owner name before adding it.";
            Log("! Extra owner was not added: blank owner.");
            return;
        }

        if (owner.Equals(GitHubUserInput.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"{owner} is already the primary owner.";
            Log($"! Extra owner was not added: {owner} is already primary.");
            return;
        }

        if (ExtraOwners.Contains(owner, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = $"{owner} is already in extra owners.";
            return;
        }

        ExtraOwners.Add(owner);
        ExtraOwnerInput = string.Empty;
        SaveSettings();
        StatusText = $"Added extra owner {owner}.";
        Log($"Extra owner added: {owner}");
    }

    private void RemoveExtraOwner(object? item)
    {
        if (item is not string owner) return;
        ExtraOwners.Remove(owner);
        SaveSettings();
        StatusText = $"Removed extra owner {owner}.";
        Log($"Extra owner removed: {owner}");
    }

    private void AddHiddenRepo()
    {
        var repo = NormalizeRepo(HiddenRepoInput);
        if (repo is null)
        {
            StatusText = "Enter a repo as owner/name before hiding it.";
            Log("! Hidden repo was not added: expected owner/name.");
            return;
        }

        if (HiddenRepos.Contains(repo, StringComparer.OrdinalIgnoreCase))
        {
            StatusText = $"{repo} is already hidden.";
            return;
        }

        HiddenRepos.Add(repo);
        HiddenRepoInput = string.Empty;
        SaveSettings();
        StatusText = $"Hidden {repo}.";
        Log($"Hidden repo added: {repo}");
    }

    private void RemoveHiddenRepo(object? item)
    {
        if (item is not string repo) return;
        HiddenRepos.Remove(repo);
        SaveSettings();
        StatusText = $"Unhid {repo}.";
        Log($"Hidden repo removed: {repo}");
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
        OnPropertyChanged(nameof(OutdatedCount));
        OnPropertyChanged(nameof(HasExtraOwners));
        OnPropertyChanged(nameof(HasHiddenRepos));
        OnPropertyChanged(nameof(HasOutdated));
        OnPropertyChanged(nameof(UpdateAllButtonLabel));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void ApplyHiddenRepoState()
    {
        var hidden = new HashSet<string>(_settings.HiddenRepos, StringComparer.OrdinalIgnoreCase);
        foreach (var app in Apps)
            app.SetHidden(hidden.Contains(app.Repo));
    }

    private void OnSettingsListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasExtraOwners));
        OnPropertyChanged(nameof(HasHiddenRepos));
    }

    private static string? NormalizeOwner(string? owner)
    {
        var value = owner?.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/')) return null;
        return value;
    }

    private static IEnumerable<string> NormalizeOwners(IEnumerable<string>? owners, string primaryOwner)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var owner in owners ?? Enumerable.Empty<string>())
        {
            var normalized = NormalizeOwner(owner);
            if (normalized is null) continue;
            if (normalized.Equals(primaryOwner, StringComparison.OrdinalIgnoreCase)) continue;
            if (seen.Add(normalized))
                yield return normalized;
        }
    }

    private static string? NormalizeRepo(string? repo)
    {
        var value = repo?.Trim().Trim('/');
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? $"{parts[0]}/{parts[1]}" : null;
    }

    private static IEnumerable<string> NormalizeRepos(IEnumerable<string>? repos)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var repo in repos ?? Enumerable.Empty<string>())
        {
            var normalized = NormalizeRepo(repo);
            if (normalized is not null && seen.Add(normalized))
                yield return normalized;
        }
    }

    private static void ReplaceCollection(ObservableCollection<string> collection, IEnumerable<string> items)
    {
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
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
