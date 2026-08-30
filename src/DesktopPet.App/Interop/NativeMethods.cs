using System.Runtime.InteropServices;

namespace DesktopPet.App.Interop;

internal static class NativeMethods
{
    internal const int GwlExStyle = -20;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExNoActivate = 0x08000000L;

    internal const int WmNcHitTest = 0x0084;
    internal const int WmHotKey = 0x0312;
    internal const int WmDisplayChange = 0x007E;
    internal const int HtTransparent = -1;

    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModNoRepeat = 0x4000;
    internal const uint VkP = 0x50;

    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint newLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(nint hWnd, int nIndex, int newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal static nint GetWindowLongPtr(nint hWnd, int index) =>
        IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, index) : new nint(GetWindowLong32(hWnd, index));

    internal static nint SetWindowLongPtr(nint hWnd, int index, nint value) =>
        IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, index, value)
            : new nint(SetWindowLong32(hWnd, index, value.ToInt32()));

    internal static int GetSignedLowWord(nint value) => unchecked((short)((long)value & 0xffff));

    internal static int GetSignedHighWord(nint value) => unchecked((short)(((long)value >> 16) & 0xffff));
}
