using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LocalDesktopStore.Services;

/// <summary>
/// Registers the background check as the current user's interactive, least-privilege
/// Task Scheduler task. No password, elevation, or service account is requested.
/// </summary>
public static class ScheduledTaskRegistrar
{
    public const string TaskName = "LocalDesktopStore scheduled update check";

    private const int TaskTriggerTime = 1;
    private const int TaskActionExec = 0;
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int TaskRunLevelLua = 0;

    public static bool Register(int intervalHours, Action<string>? log = null)
    {
        intervalHours = Math.Clamp(intervalHours, 1, 24);
        object? service = null;
        object? root = null;
        object? task = null;
        object? trigger = null;
        object? action = null;
        object? registered = null;
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Task Scheduler COM service is unavailable.");
            service = Activator.CreateInstance(serviceType)
                ?? throw new InvalidOperationException("Task Scheduler COM service could not be created.");
            dynamic scheduler = service;
            scheduler.Connect();
            root = scheduler.GetFolder("\\");
            dynamic folder = root;
            task = scheduler.NewTask(0);
            dynamic definition = task;
            definition.RegistrationInfo.Description = "Checks LocalDesktopStore for newer GitHub releases.";
            definition.Settings.Enabled = true;
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.ExecutionTimeLimit = "PT30M";
            definition.Principal.RunLevel = TaskRunLevelLua;
            definition.Principal.LogonType = TaskLogonInteractiveToken;

            trigger = definition.Triggers.Create(TaskTriggerTime);
            dynamic timeTrigger = trigger;
            timeTrigger.StartBoundary = DateTime.Now.AddMinutes(1).ToString("s", CultureInfo.InvariantCulture);
            timeTrigger.Repetition.Interval = $"PT{intervalHours}H";
            timeTrigger.Repetition.StopAtDurationEnd = false;

            var command = ResolveCommand();
            action = definition.Actions.Create(TaskActionExec);
            dynamic execAction = action;
            execAction.Path = command.Path;
            execAction.Arguments = command.Arguments;
            execAction.WorkingDirectory = command.WorkingDirectory;

            registered = folder.RegisterTaskDefinition(
                TaskName,
                task,
                TaskCreateOrUpdate,
                null,
                null,
                TaskLogonInteractiveToken,
                null);
            log?.Invoke($"Scheduled background update checks every {intervalHours} hour(s) as the current user.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"! Could not register the scheduled update check: {ex.Message}");
            return false;
        }
        finally
        {
            ReleaseCom(registered);
            ReleaseCom(action);
            ReleaseCom(trigger);
            ReleaseCom(task);
            ReleaseCom(root);
            ReleaseCom(service);
        }
    }

    public static bool Unregister(Action<string>? log = null)
    {
        object? service = null;
        object? root = null;
        try
        {
            var serviceType = Type.GetTypeFromProgID("Schedule.Service");
            if (serviceType is null)
                return true;
            service = Activator.CreateInstance(serviceType);
            if (service is null)
                return true;
            dynamic scheduler = service;
            scheduler.Connect();
            root = scheduler.GetFolder("\\");
            dynamic folder = root;
            folder.DeleteTask(TaskName, 0);
            log?.Invoke("Scheduled background update checks disabled.");
            return true;
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002)
        {
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"! Could not remove the scheduled update check: {ex.Message}");
            return false;
        }
        finally
        {
            ReleaseCom(root);
            ReleaseCom(service);
        }
    }

    private static (string Path, string Arguments, string WorkingDirectory) ResolveCommand()
    {
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current process path is unavailable.");
        var args = Environment.GetCommandLineArgs();
        var dll = Path.GetFileName(path).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? args.Skip(1).FirstOrDefault(a => a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            : null;
        if (dll is not null)
        {
            var fullDll = System.IO.Path.GetFullPath(dll);
            return (path, $"{Quote(fullDll)} --scheduled-check", System.IO.Path.GetDirectoryName(fullDll)!);
        }

        return (path, "--scheduled-check", System.IO.Path.GetDirectoryName(path)!);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); }
            catch { /* cleanup should not mask the registration result */ }
        }
    }
}
