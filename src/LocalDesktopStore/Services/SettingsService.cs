using System.IO;
using System.Text.Json;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed class SettingsService
{
    public string SettingsDir { get; }
    public string SettingsPath { get; }
    public string MachineSettingsPath { get; }
    public string AppsRootDefault { get; }
    public string CacheDir { get; }
    public string DownloadsDir { get; }
    public string LogsDir { get; }
    public string ManifestPath { get; }
    public string IconCacheDir { get; }

    private readonly InstalledManifestMigrationRunner _manifestMigrator;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsService() : this(InstalledManifestMigrationRunner.Default) { }

    public SettingsService(InstalledManifestMigrationRunner manifestMigrator)
    {
        _manifestMigrator = manifestMigrator;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDir = Path.Combine(appData, "LocalDesktopStore");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
        var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        MachineSettingsPath = Path.Combine(commonAppData, "LocalDesktopStore", "settings.json");
        AppsRootDefault = Path.Combine(localAppData, "LocalDesktopStore", "apps");
        CacheDir = Path.Combine(localAppData, "LocalDesktopStore", "cache");
        DownloadsDir = Path.Combine(localAppData, "LocalDesktopStore", "downloads");
        LogsDir = Path.Combine(localAppData, "LocalDesktopStore", "logs");
        IconCacheDir = Path.Combine(CacheDir, "icons");
        ManifestPath = Path.Combine(SettingsDir, "installed.json");
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(AppsRootDefault);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(IconCacheDir);
    }

    public string AppsRoot(AppSettings cfg)
    {
        var root = string.IsNullOrWhiteSpace(cfg.InstallRootOverride) ? AppsRootDefault : cfg.InstallRootOverride!;
        Directory.CreateDirectory(root);
        return root;
    }

    public AppSettings Load()
    {
        var settings = TryLoad(SettingsPath) ?? TryLoad(MachineSettingsPath) ?? new AppSettings();
        settings.ExtraOwners ??= new List<string>();
        settings.HiddenRepos ??= new List<string>();
        settings.UiLanguage = settings.UiLanguage?.Trim().ToLowerInvariant() switch
        {
            "en" => "en",
            "es" => "es",
            "system" => "system",
            _ => "en"
        };
        settings.SearchTopic = string.IsNullOrWhiteSpace(settings.SearchTopic) ? "windows-app" : settings.SearchTopic.Trim();
        settings.CatalogVersionPins ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        settings.CatalogVersionPins = new Dictionary<string, string>(settings.CatalogVersionPins, StringComparer.OrdinalIgnoreCase);
        settings.InstallPreferences ??= new Dictionary<string, AppInstallPreferences>(StringComparer.OrdinalIgnoreCase);
        settings.InstallPreferences = settings.InstallPreferences
            .Where(pair => pair.Value is not null && !string.IsNullOrWhiteSpace(pair.Key))
            .ToDictionary(
                pair => pair.Key.Trim(),
                pair => new AppInstallPreferences
                {
                    RunAfterInstall = pair.Value!.RunAfterInstall,
                    PinToTaskbar = pair.Value.PinToTaskbar,
                    InstallerArguments = NormalizeInstallerArguments(pair.Value.InstallerArguments)
                },
                StringComparer.OrdinalIgnoreCase);
        settings.SearchPublisherPins = PublisherPinParser.Sanitize(settings.SearchPublisherPins);
        return settings;
    }

    public void Save(AppSettings settings)
    {
        var persisted = CreatePersistedSettings(settings);
        var json = JsonSerializer.Serialize(persisted, JsonOpts);
        File.WriteAllText(SettingsPath, json);
    }

    private static AppSettings? TryLoad(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOpts) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.GitHubToken)
                && !string.IsNullOrWhiteSpace(settings.GitHubTokenProtected))
            {
                settings.GitHubToken = EnterpriseSettingsProtector.UnprotectForMachine(settings.GitHubTokenProtected);
                settings.GitHubTokenWasProtected = true;
            }

            return settings;
        }
        catch
        {
            return null;
        }
    }

    private static AppSettings CreatePersistedSettings(AppSettings settings)
    {
        var protectedToken = settings.GitHubTokenWasProtected
            ? settings.GitHubTokenProtected ?? EnterpriseSettingsProtector.ProtectForMachine(settings.GitHubToken ?? string.Empty)
            : null;

        return new AppSettings
        {
            GitHubUser = settings.GitHubUser,
            GitHubToken = settings.GitHubTokenWasProtected ? null : settings.GitHubToken,
            GitHubTokenProtected = protectedToken,
            UseTopicFilter = settings.UseTopicFilter,
            TopicFilter = settings.TopicFilter,
            EnableGitHubSearchDiscovery = settings.EnableGitHubSearchDiscovery,
            SearchTopic = settings.SearchTopic,
            ExtraOwners = new List<string>(settings.ExtraOwners ?? new()),
            HiddenRepos = new List<string>(settings.HiddenRepos ?? new()),
            VerifyHashSidecar = settings.VerifyHashSidecar,
            EnableAdvisoryChecks = settings.EnableAdvisoryChecks,
            InstallRootOverride = settings.InstallRootOverride,
            UseLightTheme = settings.UseLightTheme,
            UseSystemAccent = settings.UseSystemAccent,
            UiLanguage = settings.UiLanguage,
            EnableScheduledUpdateChecks = settings.EnableScheduledUpdateChecks,
            ScheduledUpdateIntervalHours = settings.ScheduledUpdateIntervalHours,
            CatalogVersionPins = new Dictionary<string, string>(settings.CatalogVersionPins ?? new(), StringComparer.OrdinalIgnoreCase),
            InstallPreferences = (settings.InstallPreferences ?? new Dictionary<string, AppInstallPreferences>(StringComparer.OrdinalIgnoreCase))
                .ToDictionary(
                    pair => pair.Key,
                    pair => new AppInstallPreferences
                    {
                        RunAfterInstall = pair.Value.RunAfterInstall,
                        PinToTaskbar = pair.Value.PinToTaskbar,
                        InstallerArguments = pair.Value.InstallerArguments
                    },
                    StringComparer.OrdinalIgnoreCase),
            SearchPublisherPins = new Dictionary<string, string>(settings.SearchPublisherPins ?? new(), StringComparer.OrdinalIgnoreCase)
        };
    }

    public InstalledAppsManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath))
            return new InstalledAppsManifest { Version = InstalledManifestMigrationRunner.CurrentSchemaVersion };
        var json = File.ReadAllText(ManifestPath);
        var manifest = _manifestMigrator.Load(json, JsonOpts);
        foreach (var app in manifest.Apps)
            app.InstallerArguments = NormalizeInstallerArguments(app.InstallerArguments);
        return manifest;
    }

    public void SaveManifest(InstalledAppsManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        File.WriteAllText(ManifestPath, json);
    }

    private static string? NormalizeInstallerArguments(string? arguments)
    {
        try { return InstallerArgumentParser.Normalize(arguments); }
        catch { return null; }
    }
}
