using System.Windows;
using Virtual8BitDo.App.Services;

namespace Virtual8BitDo.App;

public partial class App : Application
{
    private SingleInstance? _single;
    private TrayIcon? _tray;
    private MainWindow? _window;

    /// <summary>True quando o usuário escolheu "Sair" (senão, fechar a janela só esconde na bandeja).</summary>
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _single = new SingleInstance();
        if (!_single.TryBecomePrimary())
        {
            // Já existe uma cópia (talvez na bandeja): ela foi avisada para aparecer.
            Shutdown();
            return;
        }

        ThemeManager.Apply(dark: !AppSettings.Load().LightTheme);
        _window = new MainWindow();
        _tray = new TrayIcon(onOpen: ShowMainWindow, onExit: ExitApp);
        _single.ListenForShowRequests(() => Dispatcher.Invoke(ShowMainWindow));
        _window.Show();

        // "Virtual8BitDo.App.exe --testar-alerta" mostra o pop-up de bateria baixa, para conferir a aparência.
        if (e.Args.Contains("--testar-alerta"))
            Views.FloatingToast.Show(Views.ToastKind.Battery, "Bateria baixa = 20%", "Teste do alerta");
        // "--testar-turbo" mostra o pop-up do turbo como ele aparece ao ajustar no controle.
        if (e.Args.Contains("--testar-turbo"))
            Views.FloatingToast.Show(Views.ToastKind.Turbo, "Turbo: 12 Hz", "medido agora: 12.0 Hz");
        // "--testar-perfil" mostra o aviso de troca automática de perfil.
        if (e.Args.Contains("--testar-perfil"))
            Views.FloatingToast.Show(Views.ToastKind.Profile, "Perfil: PES 2021", "eFootball PES 2021 em foco");
    }

    public void ShowMainWindow()
    {
        if (_window == null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        // Truque do Topmost: garante que a janela venha para frente mesmo vindo da bandeja.
        _window.Topmost = true;
        _window.Activate();
        _window.Topmost = false;
        _window.Focus();
    }

    public void HideToTray() => _window?.Hide();

    public void ExitApp()
    {
        IsExiting = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }
}
