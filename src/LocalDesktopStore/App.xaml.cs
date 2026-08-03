using System.IO;
using System.Windows;
using System.Windows.Threading;
using LocalDesktopStore.Services;
using LocalDesktopStore.ViewModels;

namespace LocalDesktopStore;

public partial class App : Application
{
    private ScheduledUpdateService? _scheduledUpdates;
    private TrayIconService? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            CrashLog.Write(args.ExceptionObject as Exception);
        base.OnStartup(e);

        if (e.Args.Any(arg => arg.Equals("--scheduled-check", StringComparison.OrdinalIgnoreCase)))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunScheduledCheckAsync();
            return;
        }

        // WPF's StartupEventArgs can be empty for a WinExe launched from a redirected
        // PowerShell command. Read the process command line so CLI invocations cannot
        // fall through and construct the desktop window.
        var processArgs = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (CommandLineParser.IsCommandLine(processArgs))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunCommandLineAsync(processArgs);
            return;
        }

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if (window.DataContext is MainViewModel vm)
            StartScheduledUpdates(vm, window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scheduledUpdates?.Dispose();
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private async Task RunCommandLineAsync(string[] args)
    {
        var exitCode = await CommandLineHost.RunAsync(args, Console.Out, Console.Error);
        Console.Out.Flush();
        Console.Error.Flush();
        // A WinExe process has no dispatcher-owned window in this mode. Exit explicitly
        // after the asynchronous command completes so a WPF dispatcher cannot keep a
        // headless PowerShell invocation alive indefinitely.
        Environment.Exit(exitCode);
    }

    private void StartScheduledUpdates(MainViewModel vm, MainWindow window)
    {
        var settings = new SettingsService();
        var github = new GitHubService();
        var installer = new InstallService(settings, github);
        _trayIcon = new TrayIconService();
        _trayIcon.Activated += (_, _) =>
        {
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        };
        _scheduledUpdates = new ScheduledUpdateService(github, installer, () => vm.CurrentSettings, vm.LogMessage);
        _scheduledUpdates.UpdatesAvailable += (_, result) => _trayIcon.ShowUpdateNotification(result.Updates.Count);
        vm.SettingsSaved += (_, _) => _scheduledUpdates.Configure();
        _scheduledUpdates.Configure();
    }

    private async Task RunScheduledCheckAsync()
    {
        try
        {
            var settingsService = new SettingsService();
            var settings = settingsService.Load();
            if (!settings.EnableScheduledUpdateChecks)
                return;

            var github = new GitHubService();
            var installer = new InstallService(settingsService, github);
            var result = await ScheduledUpdateService.CheckAsync(
                settings,
                github,
                installer,
                ScheduledLog.Write);
            if (result.Updates.Count == 0)
                return;

            using var tray = new TrayIconService();
            tray.ShowUpdateNotification(result.Updates.Count);
            await Task.Delay(TimeSpan.FromSeconds(8));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails written to crash log.",
            "LocalDesktopStore",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

internal static class ScheduledLog
{
    public static void Write(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDesktopStore", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "scheduled-check.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {line}{Environment.NewLine}");
        }
        catch { /* best effort background diagnostics */ }
    }
}

internal static class CrashLog
{
    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalDesktopStore", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, ex.ToString());
        }
        catch { /* swallow — last-ditch logger */ }
    }
}
