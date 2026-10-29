using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Tempest;

internal static class WindowAppearance
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(System.Windows.Window window)
    {
        // Native caption keeps system menus, resizing and Windows snap layouts.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)) return;
        var handle = new WindowInteropHelper(window).Handle;
        var dark = 1;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var caption = 0x0033261A; // COLORREF: #1A2633
        var text = 0x00F9F2EA;
        var corners = 2;
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref corners, sizeof(int));
    }
}
