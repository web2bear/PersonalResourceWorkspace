using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace PersonalResourceWorkspace.App;

internal sealed class ExplorerFileDropHook : IDisposable
{
    private const int GwlExStyle = -20;
    private const int WsExAcceptFiles = 0x00000010;
    private const uint WmDropFiles = 0x0233;
    private const nuint SubclassId = 0x50525744;

    private readonly List<nint> _hwnds = [];
    private readonly SubclassProc _subclassProc;
    private readonly Action<IReadOnlyList<string>> _onDropped;
    private bool _disposed;

    private ExplorerFileDropHook(Action<IReadOnlyList<string>> onDropped)
    {
        _onDropped = onDropped;
        _subclassProc = OnSubclass;
    }

    public static ExplorerFileDropHook Attach(Window window, Action<IReadOnlyList<string>> onDropped)
    {
        var hook = new ExplorerFileDropHook(onDropped);
        hook.AttachCore(WindowNative.GetWindowHandle(window));
        return hook;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var hwnd in _hwnds)
        {
            RemoveWindowSubclass(hwnd, _subclassProc, SubclassId);
        }

        _hwnds.Clear();
    }

    private void AttachCore(nint root)
    {
        foreach (var hwnd in EnumerateWindows(root))
        {
            _ = RevokeDragDrop(hwnd);
            var style = GetWindowLongPtr(hwnd, GwlExStyle);
            _ = SetWindowLongPtr(hwnd, GwlExStyle, style | WsExAcceptFiles);
            DragAcceptFiles(hwnd, true);
            if (SetWindowSubclass(hwnd, _subclassProc, SubclassId, 0))
            {
                _hwnds.Add(hwnd);
            }
        }
    }

    private nint OnSubclass(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint data)
    {
        if (msg == WmDropFiles)
        {
            var paths = ReadDropFiles(wParam);
            DragFinish(wParam);
            if (paths.Count > 0)
            {
                _onDropped(paths);
            }

            return 1;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private static List<nint> EnumerateWindows(nint root)
    {
        var hwnds = new List<nint> { root };
        EnumChildWindows(root, (hwnd, _) =>
        {
            hwnds.Add(hwnd);
            return true;
        }, 0);
        return hwnds;
    }

    private static List<string> ReadDropFiles(nint drop)
    {
        var count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
        var paths = new List<string>((int)count);
        for (uint index = 0; index < count; index++)
        {
            var length = DragQueryFile(drop, index, null, 0);
            var buffer = new char[length + 1];
            var copied = DragQueryFile(drop, index, buffer, (uint)buffer.Length);
            if (copied > 0)
            {
                paths.Add(new string(buffer, 0, (int)copied));
            }
        }

        return paths;
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    private delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nint data);

    [DllImport("ole32.dll")]
    private static extern int RevokeDragDrop(nint hwnd);

    [DllImport("shell32.dll")]
    private static extern void DragAcceptFiles(nint hwnd, bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(nint hDrop, uint iFile, char[]? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    private static extern void DragFinish(nint hDrop);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hwnd, EnumWindowsProc proc, nint lParam);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassProc proc, nuint id, nint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc proc, nuint id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint hwnd, uint msg, nint wParam, nint lParam);
}
