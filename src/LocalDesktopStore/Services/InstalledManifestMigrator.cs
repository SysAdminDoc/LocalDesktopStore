using System.Text.Json;
using System.Text.Json.Nodes;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

/// <summary>
/// One step in the installed.json schema-migration chain.
/// New record fields (e.g. cert thumbprint, MSIX product family) ship as a migrator
/// from N to N+1; the runner walks the chain in order.
/// </summary>
public interface IInstalledManifestMigrator
{
    int FromVersion { get; }
    int ToVersion { get; }
    void Apply(JsonObject root);
}

public sealed class InstalledManifestMigrationRunner
{
    public const int CurrentSchemaVersion = 1;

    private readonly List<IInstalledManifestMigrator> _migrators;

    public InstalledManifestMigrationRunner(IEnumerable<IInstalledManifestMigrator>? migrators = null)
    {
        _migrators = (migrators ?? Array.Empty<IInstalledManifestMigrator>())
            .OrderBy(m => m.FromVersion)
            .ToList();
    }

    public static InstalledManifestMigrationRunner Default { get; } =
        new(Array.Empty<IInstalledManifestMigrator>());

    public InstalledAppsManifest Load(string json, JsonSerializerOptions opts)
    {
        JsonNode? parsed;
        try { parsed = JsonNode.Parse(json); }
        catch { return Empty(); }
        if (parsed is not JsonObject root) return Empty();

        var version = ReadVersion(root);
        var maxKnown = _migrators.Count == 0
            ? CurrentSchemaVersion
            : Math.Max(CurrentSchemaVersion, _migrators.Max(m => m.ToVersion));

        if (version > maxKnown)
        {
            // Forward-rolled file from a newer build — refuse to silently drop fields.
            throw new InvalidOperationException(
                $"installed.json schema version {version} is newer than this build supports ({maxKnown}). Update LocalDesktopStore.");
        }

        while (version < CurrentSchemaVersion)
        {
            var step = _migrators.FirstOrDefault(m => m.FromVersion == version);
            if (step is null)
            {
                throw new InvalidOperationException(
                    $"No migrator registered for installed.json schema {version} -> {version + 1}.");
            }
            step.Apply(root);
            version = step.ToVersion;
        }

        root["Version"] = CurrentSchemaVersion;
        InstalledAppsManifest? manifest;
        try { manifest = root.Deserialize<InstalledAppsManifest>(opts); }
        catch (JsonException) { return Empty(); }
        if (manifest is null) return Empty();
        manifest.Version = CurrentSchemaVersion;
        return manifest;
    }

    private static int ReadVersion(JsonObject root)
    {
        if (!root.TryGetPropertyValue("Version", out var node) || node is null) return 1;
        try { return node.GetValue<int>(); }
        catch { return 1; }
    }

    private static InstalledAppsManifest Empty() =>
        new() { Version = CurrentSchemaVersion };
}
