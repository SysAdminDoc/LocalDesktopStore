using System.IO;
using System.Runtime.InteropServices;

namespace LocalDesktopStore.Services;

/// <summary>
/// Creates and removes Start Menu .lnk shortcuts for portable apps. Pure COM, no WSH —
/// avoids the script-host dependency and works in restricted environments.
/// </summary>
public static class ShortcutService
{
    public static string StartMenuFolder
    {
        get
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            var dir = Path.Combine(programs, "LocalDesktopStore");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ShortcutPathFor(string displayName)
        => Path.Combine(StartMenuFolder, $"{Sanitize(displayName)}.lnk");

    public static void Create(string displayName, string targetExe, string? workingDir = null, string? description = null)
    {
        var lnkPath = ShortcutPathFor(displayName);
        var shellLink = (IShellLinkW)new ShellLink();
        try
        {
            shellLink.SetPath(targetExe);
            shellLink.SetWorkingDirectory(workingDir ?? Path.GetDirectoryName(targetExe) ?? "");
            shellLink.SetIconLocation(targetExe, 0);
            if (!string.IsNullOrEmpty(description))
                shellLink.SetDescription(description);
            ((IPersistFile)shellLink).Save(lnkPath, fRemember: false);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }

    public static void Remove(string? lnkPath)
    {
        if (string.IsNullOrEmpty(lnkPath)) return;
        try { if (File.Exists(lnkPath)) File.Delete(lnkPath); }
        catch { /* harmless — leave residual lnk if locked */ }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
                     int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
                             int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
