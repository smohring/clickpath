using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Clickpath;

/// <summary>Logs every message that can close the main window, with the foreground window at that moment.</summary>
public static class CloseTrace
{
    const int WM_CLOSE = 0x0010, WM_QUERYENDSESSION = 0x0011, WM_ENDSESSION = 0x0016, WM_SYSCOMMAND = 0x0112, SC_CLOSE = 0xF060;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(Hook);
        window.Closing += (_, e) => AppLog.Write($"main window closing (cancel={e.Cancel}); foreground: {Foreground()}");
        window.Closed += (_, _) => AppLog.Write("main window closed");
        Application.Current.SessionEnding += (_, e) => AppLog.Write($"session ending: {e.ReasonSessionEnding}");
    }

    static IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var what = msg switch
        {
            WM_CLOSE => "WM_CLOSE",
            WM_QUERYENDSESSION => "WM_QUERYENDSESSION",
            WM_ENDSESSION => "WM_ENDSESSION",
            WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SC_CLOSE => "SC_CLOSE",
            _ => null,
        };
        if (what is not null) AppLog.Write($"window message {what}; foreground: {Foreground()}");
        return IntPtr.Zero;
    }

    static string Foreground()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            GetWindowThreadProcessId(hwnd, out var pid);
            return $"{Process.GetProcessById((int)pid).ProcessName} (pid {pid}{(pid == Environment.ProcessId ? ", this app" : "")})";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return "unknown"; }
    }
}
