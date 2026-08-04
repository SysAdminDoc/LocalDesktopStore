using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public sealed class CatalogTransferDocument
{
    public int SchemaVersion { get; set; } = CatalogTransferService.CurrentSchemaVersion;
    public DateTimeOffset ExportedAt { get; set; }
    public required string PrimaryOwner { get; set; }
    public List<string> ExtraOwners { get; set; } = new();
    public bool UseTopicFilter { get; set; }
    public string TopicFilter { get; set; } = "windows-app";
    public string UiLanguage { get; set; } = "en";
    public bool EnableGitHubSearchDiscovery { get; set; }
    public string SearchTopic { get; set; } = "windows-app";
    public Dictionary<string, string> SearchPublisherPins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool VerifyHashSidecar { get; set; } = true;
    public string? InstallRootOverride { get; set; }
    public List<CatalogAppEntry> Apps { get; set; } = new();
}

public sealed class CatalogAppEntry
{
    public required string RepoOwner { get; set; }
    public required string RepoName { get; set; }
    public bool Hidden { get; set; }
    public string? InstalledVersion { get; set; }
    public string? VersionPin { get; set; }
    public ArtifactKind? InstalledKind { get; set; }
    public AppInstallPreferences? InstallPreferences { get; set; }
}

public static class CatalogTransferService
{
    public const int CurrentSchemaVersion = 1;
    private const long MaxImportBytes = 5 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Export(
        string path,
        AppSettings settings,
        IEnumerable<InstalledApp> installed)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A catalog export path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(settings.GitHubUser))
            throw new InvalidOperationException("A primary GitHub owner is required before exporting a catalog.");

        var installedByKey = installed.ToDictionary(app => app.Key, StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(settings.HiddenRepos ?? new(), StringComparer.OrdinalIgnoreCase);
        var pins = settings.CatalogVersionPins ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preferences = settings.InstallPreferences ?? new Dictionary<string, AppInstallPreferences>(StringComparer.OrdinalIgnoreCase);
        var keys = hidden
            .Concat(installedByKey.Keys)
            .Concat(pins.Keys)
            .Concat(preferences.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase);

        var document = new CatalogTransferDocument
        {
            ExportedAt = DateTimeOffset.Now,
            PrimaryOwner = settings.GitHubUser.Trim(),
            ExtraOwners = (settings.ExtraOwners ?? new()).Where(owner => !string.IsNullOrWhiteSpace(owner)).Select(owner => owner.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase).ToList(),
            UseTopicFilter = settings.UseTopicFilter,
            TopicFilter = settings.TopicFilter?.Trim() ?? string.Empty,
            UiLanguage = string.IsNullOrWhiteSpace(settings.UiLanguage) ? "en" : settings.UiLanguage.Trim(),
            EnableGitHubSearchDiscovery = settings.EnableGitHubSearchDiscovery,
            SearchTopic = string.IsNullOrWhiteSpace(settings.SearchTopic) ? "windows-app" : settings.SearchTopic.Trim(),
            SearchPublisherPins = PublisherPinParser.Sanitize(settings.SearchPublisherPins),
            VerifyHashSidecar = settings.VerifyHashSidecar,
            InstallRootOverride = settings.InstallRootOverride,
            Apps = keys.Select(key => CreateEntry(key, hidden, pins, preferences, installedByKey)).ToList()
        };

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("The catalog export path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".partial";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public static CatalogTransferDocument Import(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("The catalog file was not found.", path);
        if (new FileInfo(path).Length > MaxImportBytes)
            throw new InvalidOperationException("The catalog file is too large to import safely.");

        var document = JsonSerializer.Deserialize<CatalogTransferDocument>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("The catalog file is empty or invalid.");
        Validate(document);
        return document;
    }

    private static CatalogAppEntry CreateEntry(
        string key,
        HashSet<string> hidden,
        IReadOnlyDictionary<string, string> pins,
        IReadOnlyDictionary<string, AppInstallPreferences> preferences,
        IReadOnlyDictionary<string, InstalledApp> installed)
    {
        var separator = key.IndexOf('/');
        if (separator <= 0 || separator == key.Length - 1)
            throw new InvalidOperationException($"The catalog contains an invalid repository key: '{key}'.");
        var owner = key[..separator];
        var name = key[(separator + 1)..];
        installed.TryGetValue(key, out var installedApp);
        return new CatalogAppEntry
        {
            RepoOwner = owner,
            RepoName = name,
            Hidden = hidden.Contains(key),
            InstalledVersion = installedApp?.Version,
            VersionPin = pins.TryGetValue(key, out var pin) ? pin : installedApp?.Version,
            InstalledKind = installedApp?.Kind,
            InstallPreferences = preferences.TryGetValue(key, out var preference)
                ? new AppInstallPreferences
                {
                    RunAfterInstall = preference.RunAfterInstall,
                    PinToTaskbar = preference.PinToTaskbar,
                    InstallerArguments = preference.InstallerArguments
                }
                : null
        };
    }

    private static void Validate(CatalogTransferDocument document)
    {
        if (document.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidOperationException($"Unsupported catalog schema version {document.SchemaVersion}.");
        ValidateOwner(document.PrimaryOwner, "primary owner");
        if (document.ExtraOwners is null || document.Apps is null || document.SearchPublisherPins is null)
            throw new InvalidOperationException("The catalog file is missing required collections.");
        foreach (var owner in document.ExtraOwners)
            ValidateOwner(owner, "extra owner");
        foreach (var pin in document.SearchPublisherPins)
        {
            if (!PublisherPinParser.TryNormalize(pin.Key, pin.Value, out _, out _, out var pinError))
                throw new InvalidOperationException($"The catalog contains an invalid publisher pin: {pinError}");
        }
        if (document.Apps.Count > 10_000)
            throw new InvalidOperationException("The catalog contains too many app entries.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in document.Apps)
        {
            ValidateOwner(entry.RepoOwner, "repository owner");
            if (string.IsNullOrWhiteSpace(entry.RepoName)
                || entry.RepoName.Contains('/', StringComparison.Ordinal)
                || entry.RepoName.Contains('\\', StringComparison.Ordinal))
                throw new InvalidOperationException("The catalog contains an invalid repository name.");
            var key = $"{entry.RepoOwner}/{entry.RepoName}";
            if (!seen.Add(key))
                throw new InvalidOperationException($"The catalog contains duplicate app entry '{key}'.");
        }
    }

    private static void ValidateOwner(string? owner, string label)
    {
        if (string.IsNullOrWhiteSpace(owner)
            || owner.Contains('/', StringComparison.Ordinal)
            || owner.Contains('\\', StringComparison.Ordinal)
            || owner.Any(char.IsWhiteSpace))
            throw new InvalidOperationException($"The catalog contains an invalid {label}.");
    }
}
