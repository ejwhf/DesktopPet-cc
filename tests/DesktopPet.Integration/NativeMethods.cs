using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopPet.Integration;

internal static class NativeMethods
{
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;

    internal const long WsVisible = 0x10000000L;
    internal const long WsCaption = 0x00C00000L;
    internal const long WsThickFrame = 0x00040000L;
    internal const long WsExTopMost = 0x00000008L;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExLayered = 0x00080000L;

    internal const uint WmNcHitTest = 0x0084;
    internal const long HtTransparent = -1;
    internal const long HtClient = 1;
    private const uint SmtoAbortIfHung = 0x0002;

    internal delegate bool EnumWindowsCallback(nint window, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;

        public override readonly string ToString() =>
            $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;

        internal Point(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsCallback callback, nint parameter);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out Rect rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SendMessageTimeoutW(
        nint window,
        uint message,
        nint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

    internal static long GetWindowLong(nint window, int index)
    {
        Marshal.SetLastPInvokeError(0);
        var value = IntPtr.Size == 8
            ? GetWindowLongPtr64(window, index).ToInt64()
            : GetWindowLong32(window, index);
        var error = Marshal.GetLastPInvokeError();
        if (value == 0 && error != 0)
        {
            throw new Win32Exception(error, $"GetWindowLong({index}) failed.");
        }

        return value;
    }

    internal static long NcHitTest(nint window, Point screenPoint)
    {
        var packed = unchecked(
            (uint)(ushort)screenPoint.X |
            ((uint)(ushort)screenPoint.Y << 16));
        Marshal.SetLastPInvokeError(0);
        var succeeded = SendMessageTimeoutW(
            window,
            WmNcHitTest,
            nint.Zero,
            unchecked((nint)(int)packed),
            SmtoAbortIfHung,
            2_000,
            out var result);
        if (succeeded == nint.Zero)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, "WM_NCHITTEST timed out or failed.");
        }

        return unchecked((nint)result).ToInt64();
    }
}
