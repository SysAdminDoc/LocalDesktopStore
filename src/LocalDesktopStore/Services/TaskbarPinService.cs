using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace LocalDesktopStore.Services;

/// <summary>
/// Uses the shell context-menu verb instead of the deprecated User32 pin helper.
/// The menu is queried and invoked through COM without showing a menu or moving input.
/// </summary>
public static class TaskbarPinService
{
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint GcsVerbW = 4;
    private const uint CmfNormal = 0;
    private const int SwShownormal = 1;

    public static bool TryPin(string targetPath, IProgress<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath))
        {
            log?.Report("  ~ Could not pin to taskbar: the launch target was not found.");
            return false;
        }

        try
        {
            using var context = OpenContextMenu(targetPath);
            if (context is null)
            {
                log?.Report("  ~ Taskbar pinning is unavailable for this launch target.");
                return false;
            }

            if (context.UnpinCommand.HasValue && !context.PinCommand.HasValue)
            {
                log?.Report("  Taskbar already contains this app.");
                return true;
            }

            if (!context.PinCommand.HasValue)
            {
                log?.Report("  ~ Windows did not expose a Pin to taskbar command for this app.");
                return false;
            }

            var info = new Cminvokecommandinfoex
            {
                CbSize = (uint)Marshal.SizeOf<Cminvokecommandinfoex>(),
                FMask = CmicMaskUnicode,
                Hwnd = IntPtr.Zero,
                LpVerb = new IntPtr(context.PinCommand.Value),
                LpVerbW = new IntPtr(context.PinCommand.Value),
                NShow = SwShownormal,
                PtInvoke = default
            };
            var infoPtr = Marshal.AllocHGlobal((int)info.CbSize);
            try
            {
                Marshal.StructureToPtr(info, infoPtr, fDeleteOld: false);
                var result = context.Menu.InvokeCommand(infoPtr);
                if (result < 0)
                    Marshal.ThrowExceptionForHR(result);
            }
            finally
            {
                Marshal.FreeHGlobal(infoPtr);
            }

            log?.Report("  Pinned to the Windows taskbar.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ Taskbar pinning failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Queries the shell verb without changing taskbar state.</summary>
    public static bool CanPinTarget(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath) || !File.Exists(targetPath)) return false;
        try
        {
            using var context = OpenContextMenu(targetPath);
            return context?.PinCommand.HasValue == true || context?.UnpinCommand.HasValue == true;
        }
        catch
        {
            return false;
        }
    }

    private static ContextMenuHandle? OpenContextMenu(string targetPath)
    {
        IntPtr itemIdList = IntPtr.Zero;
        IShellFolder? parent = null;
        IContextMenu? menu = null;
        IntPtr popupMenu = IntPtr.Zero;
        try
        {
            var parseResult = SHParseDisplayName(
                targetPath,
                IntPtr.Zero,
                out itemIdList,
                0,
                out _);
            if (parseResult < 0)
                Marshal.ThrowExceptionForHR(parseResult);

            var shellFolderId = typeof(IShellFolder).GUID;
            var bindResult = SHBindToParent(
                itemIdList,
                ref shellFolderId,
                out parent,
                out var childIdList);
            if (bindResult < 0)
                Marshal.ThrowExceptionForHR(bindResult);

            var contextMenuId = typeof(IContextMenu).GUID;
            var reserved = 0u;
            var uiResult = parent.GetUIObjectOf(
                IntPtr.Zero,
                1,
                new[] { childIdList },
                ref contextMenuId,
                ref reserved,
                out menu);
            if (uiResult < 0 || menu is null)
                Marshal.ThrowExceptionForHR(uiResult < 0 ? uiResult : unchecked((int)0x80004005));

            popupMenu = CreatePopupMenu();
            if (popupMenu == IntPtr.Zero)
                throw new InvalidOperationException("Windows could not create a shell context menu.");

            var queryResult = menu!.QueryContextMenu(popupMenu, 0, 1, 0x7FFF, CmfNormal);
            if (queryResult < 0)
                Marshal.ThrowExceptionForHR(queryResult);

            var commandCount = (uint)(queryResult & 0xFFFF);
            uint? pinCommand = null;
            uint? unpinCommand = null;
            for (uint index = 0; index < commandCount; index++)
            {
                var verb = ReadVerb(menu!, index);
                if (string.IsNullOrWhiteSpace(verb)) continue;
                var normalized = NormalizeVerb(verb);
                if (normalized.Equals("pintotaskbar", StringComparison.OrdinalIgnoreCase))
                    pinCommand = index;
                else if (normalized.Equals("unpintotaskbar", StringComparison.OrdinalIgnoreCase))
                    unpinCommand = index;
            }

            var handle = new ContextMenuHandle(itemIdList, parent, menu, popupMenu, pinCommand, unpinCommand);
            itemIdList = IntPtr.Zero;
            parent = null;
            menu = null;
            popupMenu = IntPtr.Zero;
            return handle;
        }
        finally
        {
            if (popupMenu != IntPtr.Zero) DestroyMenu(popupMenu);
            if (menu is not null) Marshal.FinalReleaseComObject(menu);
            if (parent is not null) Marshal.FinalReleaseComObject(parent);
            if (itemIdList != IntPtr.Zero) CoTaskMemFree(itemIdList);
        }
    }

    private static string? ReadVerb(IContextMenu menu, uint commandOffset)
    {
        var buffer = new StringBuilder(128);
        var result = menu.GetCommandString((UIntPtr)commandOffset, GcsVerbW, IntPtr.Zero, buffer, (uint)buffer.Capacity);
        return result >= 0 ? buffer.ToString().TrimEnd('\0', ' ') : null;
    }

    private static string NormalizeVerb(string verb)
        => new(verb.Where(char.IsLetterOrDigit).ToArray());

    private sealed class ContextMenuHandle : IDisposable
    {
        public ContextMenuHandle(
            IntPtr itemIdList,
            IShellFolder parent,
            IContextMenu menu,
            IntPtr popupMenu,
            uint? pinCommand,
            uint? unpinCommand)
        {
            ItemIdList = itemIdList;
            Parent = parent;
            Menu = menu;
            PopupMenu = popupMenu;
            PinCommand = pinCommand;
            UnpinCommand = unpinCommand;
        }

        public IntPtr ItemIdList { get; }
        public IShellFolder Parent { get; }
        public IContextMenu Menu { get; }
        public IntPtr PopupMenu { get; }
        public uint? PinCommand { get; }
        public uint? UnpinCommand { get; }

        public void Dispose()
        {
            DestroyMenu(PopupMenu);
            Marshal.FinalReleaseComObject(Menu);
            Marshal.FinalReleaseComObject(Parent);
            CoTaskMemFree(ItemIdList);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Cminvokecommandinfoex
    {
        public uint CbSize;
        public uint FMask;
        public IntPtr Hwnd;
        public IntPtr LpVerb;
        public IntPtr LpParameters;
        public IntPtr LpDirectory;
        public int NShow;
        public uint DwHotKey;
        public IntPtr HIcon;
        public IntPtr LpTitle;
        public IntPtr LpVerbW;
        public IntPtr LpParametersW;
        public IntPtr LpDirectoryW;
        public IntPtr LpTitleW;
        public Point PtInvoke;
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr itemIdList, ref uint attributes);
        [PreserveSig] int EnumObjects(IntPtr hwnd, uint flags, out IntPtr enumerator);
        [PreserveSig] int BindToObject(IntPtr itemIdList, IntPtr pbc, ref Guid riid, out IntPtr result);
        [PreserveSig] int BindToStorage(IntPtr itemIdList, IntPtr pbc, ref Guid riid, out IntPtr result);
        [PreserveSig] int CompareIds(IntPtr lParam, IntPtr itemIdList1, IntPtr itemIdList2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr result);
        [PreserveSig] int GetAttributesOf(uint count, IntPtr[] itemIdLists, ref uint attributes);
        [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] itemIdLists, ref Guid riid, ref uint reserved, [MarshalAs(UnmanagedType.Interface)] out IContextMenu result);
        [PreserveSig] int GetDisplayNameOf(IntPtr itemIdList, uint flags, out IntPtr name);
        [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr itemIdList, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr result);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr menu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(IntPtr commandInfo);
        [PreserveSig] int GetCommandString(UIntPtr commandOffset, uint type, IntPtr reserved, [Out] StringBuilder name, uint maxChars);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, IntPtr bindingContext, out IntPtr itemIdList, uint attributes, out uint attributesOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHBindToParent(IntPtr itemIdList, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IShellFolder parent, out IntPtr childIdList);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoTaskMemFree(IntPtr memory);
}
