using System.IO;
using System.Text.Json;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed class SettingsService
{
    public string SettingsDir { get; }
    public string SettingsPath { get; }
    public string AppsRootDefault { get; }
    public string CacheDir { get; }
    public string DownloadsDir { get; }
    public string LogsDir { get; }
    public string ManifestPath { get; }
    public string IconCacheDir { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDir = Path.Combine(appData, "LocalDesktopStore");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
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
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(SettingsPath, json);
    }

    public InstalledAppsManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath)) return new InstalledAppsManifest();
        try
        {
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<InstalledAppsManifest>(json, JsonOpts)
                ?? new InstalledAppsManifest();
        }
        catch { return new InstalledAppsManifest(); }
    }

    public void SaveManifest(InstalledAppsManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        File.WriteAllText(ManifestPath, json);
    }
}
