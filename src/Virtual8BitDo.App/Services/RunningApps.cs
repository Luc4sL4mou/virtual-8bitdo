using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Virtual8BitDo.App.Services;

/// <summary>Um programa com janela aberta (para escolher o jogo de um perfil).</summary>
public sealed record RunningApp(string Exe, string Path, string DisplayName, string WindowTitle, ImageSource? Icon);

/// <summary>
/// Lista os programas que têm uma janela visível na tela (como a barra de tarefas mostra),
/// sem componentes do Windows e sem o próprio Virtual 8BitDo.
/// </summary>
public static class RunningApps
{
    /// <summary>Partes do Windows que nunca são "o jogo" (busca, menu Iniciar, área de trabalho...).</summary>
    private static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "SearchHost.exe", "SearchApp.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe",
        "TextInputHost.exe", "ApplicationFrameHost.exe", "LockApp.exe", "SystemSettings.exe", "ShellHost.exe",
        "Widgets.exe", "WidgetBoard.exe", "dwm.exe", "taskmgr.exe", "ScreenClippingHost.exe",
    };

    /// <summary>Componente do Windows ou o próprio Virtual 8BitDo: nunca é "o jogo".</summary>
    public static bool IsShell(string exe) =>
        Shell.Contains(exe) || exe.StartsWith("Virtual8BitDo", StringComparison.OrdinalIgnoreCase);

    /// <summary>Programas com janela aberta, os usados recentemente primeiro (na ordem de <paramref name="recentExes"/>).</summary>
    public static IReadOnlyList<RunningApp> List(IEnumerable<string> recentExes)
    {
        var byExe = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        int ownPid = Environment.ProcessId;

        EnumWindows((hwnd, _) =>
        {
            if (!IsAppWindow(hwnd)) return true;
            uint pid = WindowProcessId(hwnd);
            if (pid == ownPid) return true;
            string? path = ProcessPath((int)pid);
            if (path == null) return true;
            string exe = System.IO.Path.GetFileName(path);
            if (IsShell(exe) || byExe.ContainsKey(exe)) return true;

            string title = WindowText(hwnd);
            byExe[exe] = new RunningApp(exe, path, FriendlyName(path, title), title, IconFor(path));
            return true;
        }, IntPtr.Zero);

        var recent = recentExes.Select((e, i) => (e, i)).ToDictionary(x => x.e, x => x.i, StringComparer.OrdinalIgnoreCase);
        return byExe.Values
            .OrderBy(a => recent.TryGetValue(a.Exe, out var i) ? i : int.MaxValue)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Processo dono da janela. Apps da Microsoft Store rodam dentro do ApplicationFrameHost.exe:
    /// nesse caso o programa de verdade é o dono de uma janela filha.
    /// </summary>
    public static uint WindowProcessId(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint hostPid);
        string? hostPath = ProcessPath((int)hostPid);
        if (hostPath == null || !System.IO.Path.GetFileName(hostPath).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            return hostPid;

        uint appPid = hostPid;
        EnumChildWindows(hwnd, (child, _) =>
        {
            GetWindowThreadProcessId(child, out uint p);
            if (p == hostPid) return true;
            appPid = p;
            return false;
        }, IntPtr.Zero);
        return appPid;
    }

    /// <summary>Nome amigável e ícone de um .exe (para mostrar o jogo em foco).</summary>
    public static (string Name, ImageSource? Icon) Describe(string path, string windowTitle) => (FriendlyName(path, windowTitle), IconFor(path));

    /// <summary>Um .exe escolhido no disco (botão "Procurar...").</summary>
    public static RunningApp FromPath(string path) =>
        new(System.IO.Path.GetFileName(path), path, FriendlyName(path, ""), "", IconFor(path));

    /// <summary>
    /// Nome amigável: descrição do executável (ex.: "eFootball PES 2021"), senão o título da janela, senão o arquivo.
    /// Descrições que não dizem nada (só o nome do arquivo, "Microsoft Windows Operating System") são puladas.
    /// </summary>
    private static string FriendlyName(string path, string title)
    {
        string file = System.IO.Path.GetFileName(path);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim().Length > 1
                    && !candidate.Trim().Equals(file, StringComparison.OrdinalIgnoreCase)
                    && !candidate.Contains("Operating System", StringComparison.OrdinalIgnoreCase))
                    return candidate.Trim();
        }
        catch (Exception) { /* sem informação de versão */ }
        return !string.IsNullOrWhiteSpace(title) ? title.Trim() : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    /// <summary>Ícone do .exe em 64 px (pelo Shell do Windows, nítido); se falhar, o ícone associado de 32 px.</summary>
    private static ImageSource? IconFor(string path)
    {
        try
        {
            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory) == 0
                && factory.GetImage(new NativeSize { Width = 64, Height = 64 }, SIIGBF_ICONONLY, out IntPtr hbitmap) == 0)
            {
                try
                {
                    if (FromDib(hbitmap) is { } bmp) return bmp;
                }
                finally { DeleteObject(hbitmap); }
            }
        }
        catch (Exception) { /* cai para o método antigo */ }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon == null) return null;
            var img = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
            img.Freeze();
            return img;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Copia os pixels do bitmap do Shell mantendo a transparência (CreateBitmapSourceFromHBitmap a perde).</summary>
    private static BitmapSource? FromDib(IntPtr hbitmap)
    {
        if (GetObject(hbitmap, Marshal.SizeOf<NativeBitmap>(), out var bm) == 0 || bm.Bits == IntPtr.Zero || bm.BitsPixel != 32) return null;
        int stride = bm.WidthBytes, height = Math.Abs(bm.Height);
        var pixels = new byte[stride * height];
        Marshal.Copy(bm.Bits, pixels, 0, pixels.Length);
        var img = BitmapSource.Create(bm.Width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        // DIB "de baixo para cima": a primeira linha da memória é a última da imagem.
        BitmapSource result = bm.Height > 0 ? new TransformedBitmap(img, new ScaleTransform(1, -1)) : img;
        result.Freeze();
        return result;
    }

    /// <summary>Janela "de verdade": visível, com título, sem dono, não é janela de ferramenta e não está oculta pelo Windows.</summary>
    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    public static string WindowText(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>Caminho do .exe pelo id do processo (funciona também para jogos rodando como administrador).</summary>
    public static string? ProcessPath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    // ---- Win32 ----
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const int GW_OWNER = 4, GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, DWMWA_CLOAKED = 14;
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItemImageFactory factory);

    private const int SIIGBF_ICONONLY = 0x4;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public IntPtr Bits; }

    [DllImport("gdi32.dll")] private static extern int GetObject(IntPtr h, int size, out NativeBitmap bm);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize { public int Width, Height; }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr hbitmap);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
}
