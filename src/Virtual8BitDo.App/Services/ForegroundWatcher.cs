using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Descobre qual programa está em foco (a janela ativa) a cada meio segundo, para trocar o perfil pelo jogo.
/// Ignora a janela do próprio Virtual 8BitDo (abrir o app para editar não troca o perfil do jogo)
/// e os componentes do Windows (busca, menu Iniciar, área de trabalho...), que nunca são "o jogo".
/// </summary>
public sealed class ForegroundWatcher
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly int _ownPid = Environment.ProcessId;
    private readonly LinkedList<string> _recent = new();

    /// <summary>Nome do .exe em foco (ex.: "PES2021.exe"), sem contar o próprio app nem o Windows.</summary>
    public string? CurrentExe { get; private set; }

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
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == _ownPid) return;

        string? path = RunningApps.ProcessPath((int)pid);
        if (path == null) return;
        string exe = Path.GetFileName(path);
        if (RunningApps.IsShell(exe) || string.Equals(exe, CurrentExe, StringComparison.OrdinalIgnoreCase)) return;

        CurrentExe = exe;
        _recent.Remove(exe);
        _recent.AddFirst(exe);
        while (_recent.Count > 15) _recent.RemoveLast();
        ForegroundChanged?.Invoke(exe);
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
}
