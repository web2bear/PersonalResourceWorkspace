using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PersonalResourceWorkspace.Windows.Visuals;

internal static class WindowsShortcutResolver
{
    private const uint NoUserInterface = 0x0001;
    private const uint NoSearch = 0x0010;
    private const uint RawPath = 0x0004;

    public static string? ResolveTargetPath(string shortcutPath)
    {
        if (!File.Exists(shortcutPath) ||
            !string.Equals(Path.GetExtension(shortcutPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        object? shellLinkObject = null;
        try
        {
            shellLinkObject = new ShellLink();
            var shellLink = (IShellLinkW)shellLinkObject;
            ((IPersistFile)shellLinkObject).Load(shortcutPath, 0);
            _ = shellLink.Resolve(IntPtr.Zero, NoUserInterface | NoSearch);

            var target = new StringBuilder(32768);
            if (shellLink.GetPath(target, target.Capacity, IntPtr.Zero, RawPath) >= 0)
            {
                var expandedTarget = NormalizePath(target.ToString());
                if (File.Exists(expandedTarget) || Directory.Exists(expandedTarget))
                {
                    return expandedTarget;
                }
            }

            var iconLocation = new StringBuilder(32768);
            if (shellLink.GetIconLocation(iconLocation, iconLocation.Capacity, out _) >= 0)
            {
                var expandedIconPath = NormalizePath(iconLocation.ToString());
                if (File.Exists(expandedIconPath))
                {
                    return expandedIconPath;
                }
            }
        }
        catch (COMException)
        {
        }
        finally
        {
            if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
            {
                _ = Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }

        return null;
    }

    private static string NormalizePath(string path) =>
        Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int capacity, IntPtr findData, uint flags);

        [PreserveSig]
        int GetIDList(out IntPtr itemIdList);

        [PreserveSig]
        int SetIDList(IntPtr itemIdList);

        [PreserveSig]
        int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int capacity);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

        [PreserveSig]
        int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int capacity);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        [PreserveSig]
        int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int capacity);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        [PreserveSig]
        int GetHotkey(out short hotkey);

        [PreserveSig]
        int SetHotkey(short hotkey);

        [PreserveSig]
        int GetShowCmd(out int showCommand);

        [PreserveSig]
        int SetShowCmd(int showCommand);

        [PreserveSig]
        int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int capacity, out int iconIndex);

        [PreserveSig]
        int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        [PreserveSig]
        int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        [PreserveSig]
        int Resolve(IntPtr windowHandle, uint flags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
