using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Virtual8BitDo.App.Services;
using Virtual8BitDo.App.ViewModels;
using Virtual8BitDo.App.Views;
using Virtual8BitDo.Catalog;
using Virtual8BitDo.Core;
using Virtual8BitDo.Core.Mapping;
using Virtual8BitDo.Input;

namespace Virtual8BitDo.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel? _vm;
    private readonly AppSettings _settings = AppSettings.Load();

    public MainWindow()
    {
        InitializeComponent();

        try
        {
            // Aqui as camadas são montadas: Input (SDL) dentro do motor (Core) + controle virtual (Output) + Catalog + UI.
            var catalog = ModelCatalog.Load(Path.Combine(AppContext.BaseDirectory, "models.json"));
            var engine = new ControllerEngine(() => new SdlControllerReader(), MainViewModel.ShouldForward);
            _vm = new MainViewModel(engine, catalog, _settings); // assina os eventos antes de o motor começar
            engine.Start();
            DataContext = _vm;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Erro ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
            ((App)Application.Current).ExitApp();
            return;
        }

        // O desenho mostra o controle selecionado (botões apertados ao vivo) e é o editor visual do perfil.
        Diagram.Decorate = _vm.Profiles.Decoration;
        Diagram.ButtonClicked += id => _vm.Profiles.SelectedButton = id;
        _vm.Profiles.DiagramNeedsRefresh += () =>
        {
            Diagram.SelectedId = _vm.Profiles.SelectedButton;
            Diagram.Refresh();
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Selected)) BindDiagram();
            // Pop-up do turbo aberto: atualiza a frequência medida em tempo real.
            if (e.PropertyName == nameof(MainViewModel.MeasuredTurboText) && _vm.MeasuredTurboText.Length > 0)
                FloatingToast.UpdateSubtitle(ToastKind.Turbo, _vm.MeasuredTurboText);
        };
        Loaded += (_, _) =>
        {
            BindDiagram();
            // Etapa 8. Agendado para depois da inicialização: um diálogo modal aberto aqui dentro
            // travaria o resto do OnStartup (bandeja, instância única) até o usuário fechá-lo.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                () => AntiCheatDialog.ShowIfNeeded(this, _settings));
        };
        SourceInitialized += (_, _) => WindowEffects.Apply(this);
        Closed += (_, _) => _vm.Dispose();

        // Pop-ups flutuantes (aparecem mesmo com a janela escondida na bandeja).
        _vm.LowBattery += (controller, percent) =>
            FloatingToast.Show(ToastKind.Battery, controller.LowBatteryTitle(percent), controller.DisplayName);
        _vm.TurboAdjusted += ShowTurboToast;
        _vm.ControllerTurboDetected += d =>
            FloatingToast.Show(ToastKind.Turbo, $"Turbo do controle: {d.Hz:0} Hz",
                $"{ButtonLabels.For(d.Button, _vm.Selected)} · medido ao vivo");
    }

    private void ShowTurboToast(TurboChange change)
    {
        string subtitle = change.Button != null
            ? $"{ButtonLabels.For(change.Button, _vm?.Selected)}: turbo {(change.Enabled == true ? "ligado" : "desligado")}"
            : "Home + ↑/↓ = ±1 Hz  ·  Home + →/← = ±5 Hz";
        FloatingToast.Show(ToastKind.Turbo, $"Turbo: {change.Hz} Hz", subtitle);
    }

    private static App CurrentApp => (App)Application.Current;

    /// <summary>Minimizar funciona normalmente (barra de tarefas). Já o X esconde na bandeja; para encerrar de verdade use "Sair" no menu da bandeja.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!CurrentApp.IsExiting)
        {
            e.Cancel = true;
            CurrentApp.HideToTray();
        }
        base.OnClosing(e);
    }

    private void BindDiagram()
    {
        Diagram.Bind(_vm?.Selected);
        Diagram.SelectedId = _vm?.Profiles.SelectedButton;
    }

    // ---- Botões da interface ----

    private void RestorePhysical_Click(object sender, RoutedEventArgs e) => _vm?.RestorePhysical();
    private async void InstallViGEm_Click(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.InstallAsync(DriverInstaller.ViGEmBus); }
    private async void InstallHidHide_Click(object sender, RoutedEventArgs e) { if (_vm != null) await _vm.InstallAsync(DriverInstaller.HidHide); }

    private void NewProfile_Click(object sender, RoutedEventArgs e) => _vm?.Profiles.New();
    private void DuplicateProfile_Click(object sender, RoutedEventArgs e) => _vm?.Profiles.Duplicate();

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;
        var name = _vm.Profiles.Editing.Name;
        if (MessageBox.Show(this, $"Apagar o perfil \"{name}\"?", "Apagar perfil", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            _vm.Profiles.Delete();
    }

    private void RecentExes_DropDownOpened(object sender, EventArgs e) => _vm?.Profiles.RefreshRecent();

    /// <summary>Enter confirma o campo (nome do perfil, .exe) sem precisar clicar fora.</summary>
    private void CommitOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        var binding = sender switch
        {
            TextBox t => t.GetBindingExpression(TextBox.TextProperty),
            ComboBox c => c.GetBindingExpression(ComboBox.TextProperty),
            _ => null,
        };
        binding?.UpdateSource();
    }
}
