using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TvRemote;

/// <summary>Thin wrapper around Win32 input + window focus.</summary>
static class Native
{
    // ---- constants ----
    const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010;
    const int SW_RESTORE = 9, SM_CXSCREEN = 0, SM_CYSCREEN = 1;

    public const ushort VK_SHIFT = 0x10, VK_RETURN = 0x0D;

    // ---- structs ----
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT { public uint uMsg; public ushort wParamL, wParamH; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    // ---- imports ----
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);

    // One input action at a time, even if the phone fires requests in parallel.
    static readonly object Gate = new();

    // ---- builders ----
    static INPUT Mouse(uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dwFlags = flags } }
    };

    static INPUT Key(ushort vk, bool up, bool extended) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (extended ? KEYEVENTF_EXTENDEDKEY : 0)
            }
        }
    };

    static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0) }
        }
    };

    static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == 0)
            Console.Error.WriteLine($"SendInput failed (error {Marshal.GetLastWin32Error()}) - is the target window elevated?");
    }

    // ---- public API ----
    public static void MoveBy(int dx, int dy)
    {
        lock (Gate)
        {
            if (!GetCursorPos(out var p)) return;
            int maxX = Math.Max(1, GetSystemMetrics(SM_CXSCREEN) - 1);
            int maxY = Math.Max(1, GetSystemMetrics(SM_CYSCREEN) - 1);
            SetCursorPos(Math.Clamp(p.X + dx, 0, maxX), Math.Clamp(p.Y + dy, 0, maxY));
        }
    }

    public static void LeftClick()  { lock (Gate) Send(Mouse(MOUSEEVENTF_LEFTDOWN),  Mouse(MOUSEEVENTF_LEFTUP)); }
    public static void RightClick() { lock (Gate) Send(Mouse(MOUSEEVENTF_RIGHTDOWN), Mouse(MOUSEEVENTF_RIGHTUP)); }

    /// <summary>Press and release a key (optionally with Shift) as one atomic batch.</summary>
    public static void Tap(ushort vk, bool extended = false, bool shift = false)
    {
        lock (Gate)
        {
            var list = new List<INPUT>();
            if (shift) list.Add(Key(VK_SHIFT, false, false));
            list.Add(Key(vk, false, extended));
            list.Add(Key(vk, true, extended));
            if (shift) list.Add(Key(VK_SHIFT, true, false));
            Send(list.ToArray());
        }
    }

    /// <summary>Types literal text (no SendKeys escaping problems with + ^ % { } etc.).</summary>
    public static void TypeText(string text)
    {
        lock (Gate)
        {
            var list = new List<INPUT>(text.Length * 2);
            foreach (var c in text)
            {
                list.Add(Unicode(c, false));
                list.Add(Unicode(c, true));
            }
            Send(list.ToArray());
        }
    }

    /// <summary>Bring the first window whose process name contains <paramref name="name"/> to the front.</summary>
    public static void Focus(string name)
    {
        var proc = Process.GetProcesses().FirstOrDefault(p =>
            p.MainWindowHandle != IntPtr.Zero &&
            p.ProcessName.Contains(name, StringComparison.OrdinalIgnoreCase));
        if (proc is null) return;

        var target = proc.MainWindowHandle;
        var fg = GetForegroundWindow();
        if (fg == target) return;

        // Only restore if minimized, so a maximized/fullscreen window stays that way.
        if (IsIconic(target)) ShowWindow(target, SW_RESTORE);

        uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
        uint targetThread = GetWindowThreadProcessId(target, out _);

        if (fgThread != 0 && targetThread != 0 && fgThread != targetThread)
        {
            AttachThreadInput(fgThread, targetThread, true);
            BringWindowToTop(target);
            SetForegroundWindow(target);
            AttachThreadInput(fgThread, targetThread, false);
        }
        else
        {
            BringWindowToTop(target);
            SetForegroundWindow(target);
        }
    }
}
