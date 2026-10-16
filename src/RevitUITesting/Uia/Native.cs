using System;
using System.Runtime.InteropServices;

namespace RevitUITesting.Uia
{
    /// <summary>Win32-проверки окон: закрытость окна определяется по HWND, а не по закэшированному элементу UIA.</summary>
    internal static class Native
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        /// <summary>Окно существует и видимо.</summary>
        internal static bool IsShown(IntPtr hWnd)
        {
            return hWnd != IntPtr.Zero && IsWindow(hWnd) && IsWindowVisible(hWnd);
        }
    }
}
