using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LocalDesktopStore.Services;

/// <summary>
/// Validates and tokenizes user-supplied installer arguments using the same Windows
/// quoting rules as a native process launch. Tokens are later passed through
/// ProcessStartInfo.ArgumentList, never through a shell command line.
/// </summary>
public static class InstallerArgumentParser
{
    public const int MaxLength = 4096;
    private const string SyntheticExecutable = "LocalDesktopStore.ArgumentParser.exe";

    public static string? Normalize(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return null;
        var normalized = arguments.Trim();
        _ = Parse(normalized);
        return normalized;
    }

    public static IReadOnlyList<string> Parse(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return Array.Empty<string>();
        if (arguments.Length > MaxLength)
            throw new ArgumentException($"Installer arguments must be {MaxLength} characters or fewer.", nameof(arguments));
        if (arguments.Any(char.IsControl))
            throw new ArgumentException("Installer arguments cannot contain control characters.", nameof(arguments));

        // CommandLineToArgvW treats its first token as an executable path and applies
        // special quote handling there. Prefixing a synthetic executable makes this
        // fragment parser behave like ProcessStartInfo.ArgumentList for every token.
        var argv = CommandLineToArgvW($"{SyntheticExecutable} {arguments}", out var argc);
        if (argv == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not parse the installer arguments.");

        try
        {
            var result = new List<string>(Math.Max(0, argc - 1));
            for (var index = 1; index < argc; index++)
            {
                var item = Marshal.ReadIntPtr(argv, index * IntPtr.Size);
                result.Add(Marshal.PtrToStringUni(item) ?? string.Empty);
            }
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string commandLine,
        out int argc);

    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
