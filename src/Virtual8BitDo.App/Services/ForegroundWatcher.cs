using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Descobre qual programa está em foco (a janela ativa) a cada meio segundo, para trocar o perfil pelo jogo.
/// Ignora a janela do próprio Virtual 8BitDo (abrir o app para editar não troca o perfil do jogo)
/// e os componentes do Windows (busca, menu Iniciar, área de trabalho...), que nunca são "o jogo".
/// Quando o jogo é fechado, ele deixa de contar como "em foco" (volta o perfil Padrão), mesmo que o foco
/// tenha ido para a área de trabalho.
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly int _ownPid = Environment.ProcessId;
    private readonly LinkedList<string> _recent = new();

    // Handle do processo em foco: segurá-lo aberto impede que o Windows reaproveite o id, e diz quando ele terminou.
    private IntPtr _currentProcess;

    /// <summary>Nome do .exe em foco (ex.: "PES2021.exe"), sem contar o próprio app nem o Windows.</summary>
    public string? CurrentExe { get; private set; }

    /// <summary>Caminho completo do .exe em foco (para o ícone e o nome amigável).</summary>
    public string? CurrentPath { get; private set; }

    /// <summary>Título da janela em foco quando ela entrou em foco (nome amigável quando o .exe não tem descrição).</summary>
    public string CurrentTitle { get; private set; } = "";

    /// <summary>Programas que estiveram em foco recentemente (mais recente primeiro): ordenam a lista de "Novo perfil".</summary>
    public IReadOnlyCollection<string> RecentExes => _recent;

    public event Action<string?>? ForegroundChanged;

    public ForegroundWatcher()
    {
        _timer.Tick += (_, _) => Check();
        _timer.Start();
    }

    private void Check()
    {
        if (CurrentExe != null && HasExited(_currentProcess))
        {
            SetCurrent(null, null, IntPtr.Zero);
            return;
        }

        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        uint pid = RunningApps.WindowProcessId(hwnd);
        if (pid == 0 || pid == _ownPid) return;

        string? path = RunningApps.ProcessPath((int)pid);
        if (path == null) return;
        string exe = Path.GetFileName(path);
        if (RunningApps.IsShell(exe) || string.Equals(exe, CurrentExe, StringComparison.OrdinalIgnoreCase)) return;

        _recent.Remove(exe);
        _recent.AddFirst(exe);
        while (_recent.Count > 15) _recent.RemoveLast();
        CurrentTitle = RunningApps.WindowText(hwnd);
        SetCurrent(exe, path, OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid));
    }

    private void SetCurrent(string? exe, string? path, IntPtr process)
    {
        if (_currentProcess != IntPtr.Zero) CloseHandle(_currentProcess);
        _currentProcess = process;
        CurrentExe = exe;
        CurrentPath = path;
        ForegroundChanged?.Invoke(exe);
    }

    private static bool HasExited(IntPtr process) =>
        process == IntPtr.Zero || (GetExitCodeProcess(process, out uint code) && code != STILL_ACTIVE);

    public void Dispose()
    {
        _timer.Stop();
        if (_currentProcess != IntPtr.Zero) CloseHandle(_currentProcess);
        _currentProcess = IntPtr.Zero;
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, STILL_ACTIVE = 259;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
}
