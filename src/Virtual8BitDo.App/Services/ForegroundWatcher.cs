using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Descobre qual programa está em foco (a janela ativa) a cada meio segundo, para trocar o perfil pelo jogo.
/// A janela do próprio Virtual 8BitDo é ignorada: abrir o app para editar não troca o perfil do jogo.
/// </summary>
public sealed class ForegroundWatcher
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly int _ownPid = Environment.ProcessId;
    private readonly LinkedList<string> _recent = new();

    /// <summary>Nome do .exe em foco (ex.: "eldenring.exe"), sem contar o próprio app.</summary>
    public string? CurrentExe { get; private set; }

    /// <summary>Programas que estiveram em foco recentemente (mais recente primeiro), para criar perfis.</summary>
    public IReadOnlyCollection<string> RecentExes => _recent;

    public event Action<string?>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == _ownPid) return;

        string? exe = ExeName((int)pid);
        if (exe == null || string.Equals(exe, CurrentExe, StringComparison.OrdinalIgnoreCase)) return;
        if (exe.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) { SetCurrent(exe, remember: false); return; }
        SetCurrent(exe, remember: true);
    }

    private void SetCurrent(string exe, bool remember)
    {
        CurrentExe = exe;
        if (remember)
        {
            _recent.Remove(exe);
            _recent.AddFirst(exe);
            while (_recent.Count > 10) _recent.RemoveLast();
        }
        ForegroundChanged?.Invoke(exe);
    }

    /// <summary>Nome do .exe pelo id do processo (funciona também para jogos que rodam como administrador).</summary>
    private static string? ExeName(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? Path.GetFileName(sb.ToString()) : null;
        }
        finally { CloseHandle(h); }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, int flags, StringBuilder name, ref int size);
}
