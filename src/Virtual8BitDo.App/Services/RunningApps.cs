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
            GetWindowThreadProcessId(hwnd, out uint pid);
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

    /// <summary>Um .exe escolhido no disco (botão "Procurar...").</summary>
    public static RunningApp FromPath(string path) =>
        new(System.IO.Path.GetFileName(path), path, FriendlyName(path, ""), "", IconFor(path));

    /// <summary>Nome amigável: descrição do executável (ex.: "eFootball PES 2021"), senão o título da janela, senão o arquivo.</summary>
    private static string FriendlyName(string path, string title)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            foreach (var candidate in new[] { info.FileDescription, info.ProductName })
                if (!string.IsNullOrWhiteSpace(candidate) && candidate.Trim().Length > 1) return candidate.Trim();
        }
        catch (Exception) { /* sem informação de versão */ }
        return !string.IsNullOrWhiteSpace(title) ? title.Trim() : System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static ImageSource? IconFor(string path)
    {
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

    /// <summary>Janela "de verdade": visível, com título, sem dono, não é janela de ferramenta e não está oculta pelo Windows.</summary>
    private static bool IsAppWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
        if ((GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) return false;
        if (GetWindowTextLength(hwnd) == 0) return false;
        return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    private static string WindowText(IntPtr hwnd)
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
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
}
