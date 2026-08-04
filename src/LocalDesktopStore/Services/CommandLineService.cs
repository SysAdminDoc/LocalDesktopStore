using System.IO;
using System.Text.Json;
using LocalDesktopStore.Models;

namespace LocalDesktopStore.Services;

public enum CliCommand
{
    Help,
    Version,
    List,
    Refresh,
    Install,
    Uninstall,
    Run
}

public sealed record CommandLineOptions(
    CliCommand Command,
    string? Repository,
    bool Json,
    string? InstallerArguments);

public static class CommandLineParser
{
    public const string Usage = """
        LocalDesktopStore — private GitHub desktop-app catalog

        Usage:
          LocalDesktopStore.exe                         Open the desktop UI
          LocalDesktopStore.exe --install OWNER/REPO    Discover and install a release
          LocalDesktopStore.exe --uninstall OWNER/REPO  Uninstall a tracked app
          LocalDesktopStore.exe --run OWNER/REPO        Launch a tracked app
          LocalDesktopStore.exe --refresh                Discover configured owners
          LocalDesktopStore.exe --list                   List tracked installs
          LocalDesktopStore.exe --version                Print the application version
          LocalDesktopStore.exe --help                   Show this help

        Options:
          --json                       Emit one machine-readable JSON result
          --installer-args "ARGS"      Override installer switches for --install

        Exit codes:
          0 success, 2 invalid arguments, 3 target not found, 4 operation failed,
          5 cancelled
        """;

    public static bool IsCommandLine(IReadOnlyList<string> args)
        => args.Any(arg => IsCommandLineOptionName(GetOptionName(arg)));

    public static bool TryParse(
        IReadOnlyList<string> args,
        out CommandLineOptions options,
        out string? error)
    {
        options = new CommandLineOptions(CliCommand.Help, null, false, null);
        error = null;

        CliCommand? command = null;
        string? repository = null;
        string? installerArguments = null;
        var json = false;
        var headless = false;

        for (var i = 0; i < args.Count; i++)
        {
            var (name, inlineValue) = SplitOption(args[i]);
            switch (name)
            {
                case "--headless":
                    if (inlineValue is not null)
                        return Fail("--headless does not take a value.", out options, out error);
                    headless = true;
                    break;
                case "-h":
                case "--help":
                    if (!TrySetCommand(CliCommand.Help, ref command, out error))
                        return false;
                    break;
                case "--version":
                    if (!TrySetCommand(CliCommand.Version, ref command, out error))
                        return false;
                    break;
                case "--list":
                    if (!TrySetCommand(CliCommand.List, ref command, out error))
                        return false;
                    break;
                case "--refresh":
                    if (!TrySetCommand(CliCommand.Refresh, ref command, out error))
                        return false;
                    break;
                case "--install":
                    if (!TrySetCommand(CliCommand.Install, ref command, out error))
                        return false;
                    if (!TryReadValue(args, ref i, inlineValue, "--install", out repository, out error))
                        return false;
                    break;
                case "--uninstall":
                    if (!TrySetCommand(CliCommand.Uninstall, ref command, out error))
                        return false;
                    if (!TryReadValue(args, ref i, inlineValue, "--uninstall", out repository, out error))
                        return false;
                    break;
                case "--run":
                    if (!TrySetCommand(CliCommand.Run, ref command, out error))
                        return false;
                    if (!TryReadValue(args, ref i, inlineValue, "--run", out repository, out error))
                        return false;
                    break;
                case "--json":
                    if (inlineValue is not null)
                        return Fail("--json does not take a value.", out options, out error);
                    json = true;
                    break;
                case "--installer-args":
                case "--arguments":
                    if (!TryReadValue(args, ref i, inlineValue, name, out installerArguments, out error))
                        return false;
                    break;
                default:
                    return Fail($"Unknown option '{args[i]}'.", out options, out error);
            }
        }

        if (command is null)
        {
            if (!headless)
                return Fail("A CLI command is required when command-line options are supplied.", out options, out error);
            command = CliCommand.Help;
        }

        if (command is CliCommand.Install or CliCommand.Uninstall or CliCommand.Run)
        {
            if (!TryNormalizeRepository(repository, out var normalizedRepository))
                return Fail("Repository must use the OWNER/REPO form.", out options, out error);
            repository = normalizedRepository;
        }
        else if (!string.IsNullOrWhiteSpace(repository))
        {
            return Fail("Only --install, --uninstall, and --run accept a repository.", out options, out error);
        }

        if (!string.IsNullOrWhiteSpace(installerArguments) && command != CliCommand.Install)
            return Fail("--installer-args is only valid with --install.", out options, out error);

        if (!string.IsNullOrWhiteSpace(installerArguments))
        {
            try
            {
                installerArguments = InstallerArgumentParser.Normalize(installerArguments);
            }
            catch (Exception ex)
            {
                return Fail($"Invalid installer arguments: {ex.Message}", out options, out error);
            }
        }

        options = new CommandLineOptions(command.Value, repository, json, installerArguments);
        return true;
    }

    private static bool TrySetCommand(CliCommand requested, ref CliCommand? command, out string? error)
    {
        if (command is not null && command != requested)
        {
            error = "Choose one CLI command at a time.";
            return false;
        }

        command = requested;
        error = null;
        return true;
    }

    private static bool TryReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string? inlineValue,
        string option,
        out string? value,
        out string? error)
    {
        if (inlineValue is not null)
        {
            value = inlineValue;
        }
        else if (index + 1 < args.Count)
        {
            value = args[++index];
        }
        else
        {
            value = null;
            error = $"{option} requires a value.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("-", StringComparison.Ordinal))
        {
            error = $"{option} requires a value.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryNormalizeRepository(string? value, out string? normalized)
    {
        normalized = null;
        var parts = value?.Trim().Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts is not { Length: 2 } || parts.Any(string.IsNullOrWhiteSpace))
            return false;
        if (parts.Any(part => part.Any(char.IsWhiteSpace) || part.Contains('\\') || part.Contains('?') || part.Contains('#')))
            return false;
        normalized = $"{parts[0]}/{parts[1]}";
        return true;
    }

    private static string GetOptionName(string argument)
        => SplitOption(argument).Name;

    private static bool IsCommandLineOptionName(string name)
        => name is "--headless" or "-h" or "--help" or "--version" or "--list" or "--refresh"
            or "--install" or "--uninstall" or "--run" or "--json" or "--installer-args" or "--arguments";

    private static (string Name, string? InlineValue) SplitOption(string argument)
    {
        var equals = argument.IndexOf('=');
        return equals < 0
            ? (argument, null)
            : (argument[..equals], argument[(equals + 1)..]);
    }

    private static bool Fail(
        string message,
        out CommandLineOptions options,
        out string? error)
    {
        options = new CommandLineOptions(CliCommand.Help, null, false, null);
        error = message;
        return false;
    }
}

public static class CommandLineHost
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default)
    {
        if (!CommandLineParser.TryParse(args, out var options, out var parseError))
        {
            error.WriteLine($"Error: {parseError}");
            error.WriteLine(CommandLineParser.Usage);
            return 2;
        }

        if (options.Command == CliCommand.Help)
        {
            output.WriteLine(CommandLineParser.Usage);
            return 0;
        }

        if (options.Command == CliCommand.Version)
        {
            output.WriteLine($"LocalDesktopStore {typeof(CommandLineHost).Assembly.GetName().Version?.ToString(3) ?? "unknown"}");
            return 0;
        }

        var messages = new List<string>();
        void Report(string line)
        {
            messages.Add(line);
            if (!options.Json)
                output.WriteLine(line);
        }

        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            var github = new GitHubService();
            var installer = new InstallService(settingsService, github);
            var log = new InlineProgress<string>(Report);
            var bytes = new InlineProgress<long>(_ => { });

            var exitCode = options.Command switch
            {
                CliCommand.List => RunList(options, installer, output),
                CliCommand.Refresh => await RunRefreshAsync(options, settings, github, log, output, ct),
                CliCommand.Install => await RunInstallAsync(options, settings, installer, github, log, bytes, output, ct),
                CliCommand.Uninstall => await RunUninstallAsync(options, installer, log, output, ct),
                CliCommand.Run => RunApp(options, installer, log, output),
                _ => 2
            };
            return exitCode;
        }
        catch (OperationCanceledException)
        {
            if (options.Json)
                WriteJson(output, new { ok = false, command = options.Command.ToString().ToLowerInvariant(), error = "Operation cancelled." });
            else
                error.WriteLine("Operation cancelled.");
            return 5;
        }
        catch (Exception ex)
        {
            if (options.Json)
                WriteJson(output, new { ok = false, command = options.Command.ToString().ToLowerInvariant(), error = ex.Message, messages });
            else
                error.WriteLine($"Error: {ex.Message}");
            return 4;
        }
    }

    private static int RunList(
        CommandLineOptions options,
        InstallService installer,
        TextWriter output)
    {
        if (options.Json)
        {
            WriteJson(output, new
            {
                ok = true,
                command = "list",
                apps = installer.Installed.Select(app => new
                {
                    repo = app.Key,
                    app.Version,
                    kind = app.Kind.ToString(),
                    app.InstalledAt
                })
            });
            return 0;
        }

        if (installer.Installed.Count == 0)
        {
            output.WriteLine("No tracked installs.");
            return 0;
        }

        foreach (var app in installer.Installed.OrderBy(app => app.Key, StringComparer.OrdinalIgnoreCase))
            output.WriteLine($"{app.Key} v{app.Version} [{app.Kind.DisplayName()}]");
        return 0;
    }

    private static async Task<int> RunRefreshAsync(
        CommandLineOptions options,
        AppSettings settings,
        GitHubService github,
        IProgress<string> log,
        TextWriter output,
        CancellationToken ct)
    {
        var apps = await github.DiscoverAsync(settings, log, ct);
        if (options.Json)
        {
            WriteJson(output, new
            {
                ok = true,
                command = "refresh",
                apps = apps.Select(ToCatalogObject)
            });
            return 0;
        }

        foreach (var app in apps.OrderBy(app => app.RepoOwner).ThenBy(app => app.RepoName))
            output.WriteLine($"{app.RepoOwner}/{app.RepoName} v{app.DisplayVersion} [{app.Kind.DisplayName()}] {app.AssetName}");
        output.WriteLine($"Discovered {apps.Count} app(s).");
        return 0;
    }

    private static async Task<int> RunInstallAsync(
        CommandLineOptions options,
        AppSettings settings,
        InstallService installer,
        GitHubService github,
        IProgress<string> log,
        IProgress<long> bytes,
        TextWriter output,
        CancellationToken ct)
    {
        var (owner, _) = SplitRepository(options.Repository!);
        var operationSettings = CopySettingsForOwner(settings, owner);
        if (options.InstallerArguments is not null)
        {
            operationSettings.InstallPreferences[options.Repository!] = new AppInstallPreferences
            {
                InstallerArguments = options.InstallerArguments
            };
        }

        var apps = await github.DiscoverAsync(operationSettings, log, ct);
        var info = apps.FirstOrDefault(app => app.RepoOwner.Equals(owner, StringComparison.OrdinalIgnoreCase)
            && app.RepoName.Equals(SplitRepository(options.Repository!).Repo, StringComparison.OrdinalIgnoreCase));
        if (info is null)
        {
            WriteNotFound(options, output, options.Repository!);
            return 3;
        }

        var record = await installer.InstallAsync(info, operationSettings, log, bytes, ct);
        if (record is null)
        {
            if (options.Json)
                WriteJson(output, new { ok = true, command = "install", repo = $"{info.RepoOwner}/{info.RepoName}" });
            else
                output.WriteLine($"App Installer handoff started for {info.RepoOwner}/{info.RepoName}.");
            return 0;
        }

        if (operationSettings.InstallPreferences.TryGetValue(record.Key, out var preference))
        {
            if (preference.RunAfterInstall)
                installer.TryRun(record, log);
            if (preference.PinToTaskbar)
                installer.TryPinToTaskbar(record, log);
        }

        if (options.Json)
        {
            WriteJson(output, new
            {
                ok = true,
                command = "install",
                repo = record.Key,
                record.Version,
                kind = record.Kind.ToString()
            });
        }
        else
        {
            output.WriteLine($"Installed {record.Key} v{record.Version} ({record.Kind.DisplayName()}).");
        }

        return 0;
    }

    private static async Task<int> RunUninstallAsync(
        CommandLineOptions options,
        InstallService installer,
        IProgress<string> log,
        TextWriter output,
        CancellationToken ct)
    {
        var app = installer.Find(SplitRepository(options.Repository!).Owner, SplitRepository(options.Repository!).Repo);
        if (app is null)
        {
            WriteNotFound(options, output, options.Repository!);
            return 3;
        }

        await installer.UninstallAsync(app, log, ct);
        if (options.Json)
            WriteJson(output, new { ok = true, command = "uninstall", repo = options.Repository });
        else
            output.WriteLine($"Uninstalled {options.Repository}.");
        return 0;
    }

    private static int RunApp(
        CommandLineOptions options,
        InstallService installer,
        IProgress<string> log,
        TextWriter output)
    {
        var parts = SplitRepository(options.Repository!);
        var app = installer.Find(parts.Owner, parts.Repo);
        if (app is null)
        {
            WriteNotFound(options, output, options.Repository!);
            return 3;
        }

        var launched = installer.TryRun(app, log);
        if (options.Json)
            WriteJson(output, new { ok = launched, command = "run", repo = options.Repository });
        else if (launched)
            output.WriteLine($"Launched {options.Repository}.");
        return launched ? 0 : 4;
    }

    private static AppSettings CopySettingsForOwner(AppSettings source, string owner)
        => new()
        {
            GitHubUser = owner,
            GitHubToken = source.GitHubToken,
            GitHubTokenProtected = source.GitHubTokenProtected,
            GitHubTokenWasProtected = source.GitHubTokenWasProtected,
            UseTopicFilter = source.UseTopicFilter,
            TopicFilter = source.TopicFilter,
            HiddenRepos = new List<string>(source.HiddenRepos),
            VerifyHashSidecar = source.VerifyHashSidecar,
            EnableAdvisoryChecks = source.EnableAdvisoryChecks,
            InstallRootOverride = source.InstallRootOverride,
            UseLightTheme = source.UseLightTheme,
            UseSystemAccent = source.UseSystemAccent,
            EnableScheduledUpdateChecks = source.EnableScheduledUpdateChecks,
            ScheduledUpdateIntervalHours = source.ScheduledUpdateIntervalHours,
            CatalogVersionPins = new Dictionary<string, string>(source.CatalogVersionPins, StringComparer.OrdinalIgnoreCase),
            InstallPreferences = source.InstallPreferences.ToDictionary(
                pair => pair.Key,
                pair => new AppInstallPreferences
                {
                    RunAfterInstall = pair.Value.RunAfterInstall,
                    PinToTaskbar = pair.Value.PinToTaskbar,
                    InstallerArguments = pair.Value.InstallerArguments
                },
                StringComparer.OrdinalIgnoreCase)
        };

    private static (string Owner, string Repo) SplitRepository(string repository)
    {
        var parts = repository.Split('/', 2, StringSplitOptions.TrimEntries);
        return (parts[0], parts[1]);
    }

    private static object ToCatalogObject(AppInfo app)
        => new
        {
            repo = $"{app.RepoOwner}/{app.RepoName}",
            version = app.DisplayVersion,
            kind = app.Kind.ToString(),
            app.AssetName,
            app.AssetUrl
        };

    private static void WriteNotFound(CommandLineOptions options, TextWriter output, string repository)
    {
        if (options.Json)
            WriteJson(output, new { ok = false, error = $"No discovered or tracked app matched {repository}." });
        else
            output.WriteLine($"No discovered or tracked app matched {repository}.");
    }

    private static void WriteJson(TextWriter output, object payload)
        => output.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
