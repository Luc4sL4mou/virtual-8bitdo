using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Barra de título do Windows 11 no tema do app: escura ou clara, e com a mesma cor do fundo
/// (assim ela some e a janela parece uma peça só). Em Windows mais antigo, só o modo escuro/claro vale.
/// </summary>
public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;

        int dark = ThemeManager.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (Application.Current.TryFindResource("BgColor") is not Color bg) return;
        int caption = ToColorRef(bg);
        int text = ToColorRef(ThemeManager.IsDark ? Color.FromRgb(0xF5, 0xF5, 0xFA) : Color.FromRgb(0x15, 0x13, 0x1F));
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    /// <summary>COLORREF do Win32: 0x00BBGGRR.</summary>
    private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);
}
