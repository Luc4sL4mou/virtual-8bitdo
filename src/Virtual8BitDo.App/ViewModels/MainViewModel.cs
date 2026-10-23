using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Virtual8BitDo.App.Services;
using Virtual8BitDo.Catalog;
using Virtual8BitDo.Core;
using Virtual8BitDo.Core.Mapping;
using Virtual8BitDo.Core.Profiles;
using Virtual8BitDo.Input;
using Virtual8BitDo.Output;

namespace Virtual8BitDo.App.ViewModels;

/// <summary>
/// Liga as camadas: motor (Core: leitura + controle virtual) + catálogo (Catalog) + HidHide (Output) + tela.
/// O motor roda na thread dele; aqui a tela só consulta o último estado 60x por segundo.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ControllerEngine _engine;
    private readonly ModelCatalog _catalog;
    private readonly AppSettings _settings;
    private HidHideService _hidHide = new();
    private string? _installStatus;
    private bool _installing;
    private string _measuredTurboText = "";
    private readonly SemaphoreSlim _hidHideLock = new(1, 1);
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<int, BatteryAlertPolicy> _batteryAlerts = new();
    private ControllerViewModel? _selected;
    private int _tick;

    /// <summary>Disparado quando um controle sem fio precisa avisar bateria baixa (controle, nível em %).</summary>
    public event Action<ControllerViewModel, int>? LowBattery;

    /// <summary>O atalho no controle mudou o turbo (já na thread da tela): a janela mostra o pop-up.</summary>
    public event Action<TurboChange>? TurboAdjusted;

    /// <summary>O próprio controle está gerando turbo (Estrela + botão): a janela mostra o pop-up com a frequência.</summary>
    public event Action<TurboDetection>? ControllerTurboDetected;

    public MainViewModel(ControllerEngine engine, ModelCatalog catalog, AppSettings settings)
    {
        _engine = engine;
        _catalog = catalog;
        _settings = settings;
        _engine.Forwarding = settings.Forwarding;

        Foreground = new ForegroundWatcher();
        Profiles = new ProfilesViewModel(new ProfileStore(), engine, Foreground, () => Selected);

        // Os eventos vêm da thread do motor: repassamos para a thread da tela.
        var dispatcher = Application.Current.Dispatcher;
        _engine.ControllerConnected += info => dispatcher.BeginInvoke(() => OnConnected(info));
        _engine.ControllerDisconnected += id => dispatcher.BeginInvoke(() => OnDisconnected(id));
        _engine.TurboChanged += change => dispatcher.BeginInvoke(() =>
        {
            Profiles.ApplyFromController(change);
            TurboAdjusted?.Invoke(change);
        });
        _engine.ControllerTurboDetected += detection => dispatcher.BeginInvoke(() => ControllerTurboDetected?.Invoke(detection));

        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();

        // Se o app foi fechado à força antes, o controle pode ter ficado escondido: começa sempre visível.
        _ = ResetHidingAsync();
    }

    private async Task ResetHidingAsync()
    {
        if (!_hidHide.IsInstalled) return;
        await _hidHideLock.WaitAsync(); // pega a vez antes de qualquer "esconder" disparado por conexão
        try { await Task.Run(() => _hidHide.Unhide()); }
        finally { _hidHideLock.Release(); }
    }

    /// <summary>Só controles 8BitDo ganham controle virtual (e assim o próprio controle virtual nunca é repassado de novo).</summary>
    public static bool ShouldForward(ControllerInfo info) => info.Vid == 0x2DC8;

    public ObservableCollection<ControllerViewModel> Controllers { get; } = new();

    public ForegroundWatcher Foreground { get; }
    public ProfilesViewModel Profiles { get; }

    public ControllerViewModel? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(HasNoSelection));
            Profiles.RebuildEditor(); // nomes e disponibilidade dos extras dependem do controle
        }
    }

    // ---- Turbo ----

    /// <summary>Frequência real medida agora (só enquanto um botão com turbo está segurado).</summary>
    public string MeasuredTurboText { get => _measuredTurboText; private set => Set(ref _measuredTurboText, value); }

    // ---- Instalação dos drivers (opção B: direto da fonte oficial, com verificação de assinatura) ----

    public bool IsViGEmMissing => _engine.BusStatus == VirtualBusStatus.DriverMissing;
    public bool IsHidHideMissing => !_hidHide.IsInstalled;
    public bool CanInstall => !_installing;
    public string? InstallStatus { get => _installStatus; private set { Set(ref _installStatus, value); OnPropertyChanged(nameof(HasInstallStatus)); } }
    public bool HasInstallStatus => !string.IsNullOrEmpty(_installStatus);

    public async Task InstallAsync(DriverInstaller.Driver driver)
    {
        _installing = true;
        OnPropertyChanged(nameof(CanInstall));
        var error = await DriverInstaller.InstallAsync(driver, new Progress<string>(s => InstallStatus = s));
        _installing = false;
        OnPropertyChanged(nameof(CanInstall));

        if (error != null) { InstallStatus = error; return; }
        if (driver == DriverInstaller.HidHide)
        {
            _hidHide = new HidHideService(); // agora instalado: passa a ser usado sem reiniciar
            InstallStatus = "HidHide instalado.";
            OnPropertyChanged(nameof(IsHidHideMissing));
            _ = UpdateHidingAsync();
        }
        else
        {
            InstallStatus = "ViGEmBus instalado. Feche o app pelo menu da bandeja (Sair) e abra de novo para ativar o controle virtual.";
        }
        RefreshVirtualStatus();
    }

    public bool HasSelection => Selected != null;
    public bool HasNoSelection => Selected == null;
    public bool HasNoControllers => Controllers.Count == 0;
    public int ControllerCount => Controllers.Count;

    public string StatusText => _catalog.LoadError
        ?? $"{_catalog.Models.Count} modelos no catálogo";

    // ---- Controle virtual + HidHide ----

    /// <summary>Repassar o controle físico para o controle virtual Xbox 360.</summary>
    public bool Forwarding
    {
        get => _engine.Forwarding;
        set
        {
            if (_engine.Forwarding == value) return;
            _engine.Forwarding = value;
            _settings.Forwarding = value;
            _settings.Save();
            OnPropertyChanged();
            RefreshVirtualStatus();
            _ = UpdateHidingAsync();
        }
    }

    public string VirtualStatusText => _engine.BusStatus switch
    {
        VirtualBusStatus.DriverMissing => "Driver ViGEmBus não instalado.",
        VirtualBusStatus.Error => $"Erro no ViGEmBus: {_engine.BusError}",
        _ when !Forwarding => "Desligado.",
        _ when _engine.VirtualPadCount == 0 => "Pronto. Aguardando um controle 8BitDo.",
        _ => $"Ativo: {_engine.VirtualPadCount} controle(s) virtual(is) Xbox 360.",
    };

    public string HidHideStatusText => _hidHide.Status switch
    {
        HidHideStatus.NotInstalled => "HidHide não instalado: os jogos podem ver o controle duas vezes.",
        HidHideStatus.Hiding => $"Controle físico escondido dos jogos ({_hidHide.HiddenDeviceCount} dispositivo(s)).",
        HidHideStatus.Error => $"Erro no HidHide: {_hidHide.LastError}",
        _ => "Controle físico visível para os jogos.",
    };

    public bool IsVirtualActive => Forwarding && _engine.VirtualPadCount > 0;
    public bool IsHiding => _hidHide.Status == HidHideStatus.Hiding;

    /// <summary>Emergência: desliga o repasse e mostra o controle físico de novo para os jogos.</summary>
    public void RestorePhysical() => Forwarding = false;

    private void RefreshVirtualStatus()
    {
        OnPropertyChanged(nameof(VirtualStatusText));
        OnPropertyChanged(nameof(HidHideStatusText));
        OnPropertyChanged(nameof(IsVirtualActive));
        OnPropertyChanged(nameof(IsHiding));
        OnPropertyChanged(nameof(IsViGEmMissing));
        OnPropertyChanged(nameof(IsHidHideMissing));
    }

    /// <summary>
    /// Modo automático: esconde o controle físico quando há repasse ativo para um 8BitDo; senão, mostra.
    /// Roda fora da thread da tela (cada chamada ao HidHide leva ~100 ms) e uma de cada vez.
    /// </summary>
    private async Task UpdateHidingAsync()
    {
        if (!_hidHide.IsInstalled) return;
        bool shouldHide = Forwarding
            && _engine.BusStatus == VirtualBusStatus.Ready
            && Controllers.Any(c => ShouldForward(c.Info));

        await _hidHideLock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                if (shouldHide) _hidHide.Hide(path => path.Contains("2DC8", StringComparison.OrdinalIgnoreCase));
                else if (_hidHide.Status == HidHideStatus.Hiding) _hidHide.Unhide();
            });
        }
        finally { _hidHideLock.Release(); }
        RefreshVirtualStatus();
    }

    // ---- Conexões ----

    private void OnConnected(ControllerInfo info)
    {
        var known = _catalog.Find(info.Vid, info.Pid);
        var vm = new ControllerViewModel(info, known, _catalog.ManualChoices);
        Controllers.Add(vm);
        Selected ??= vm;
        OnPropertyChanged(nameof(HasNoControllers));
        OnPropertyChanged(nameof(ControllerCount));
        if (ShouldForward(info)) _ = UpdateHidingAsync();
    }

    private void OnDisconnected(int instanceId)
    {
        var vm = Controllers.FirstOrDefault(c => c.Info.InstanceId == instanceId);
        if (vm == null) return;
        Controllers.Remove(vm);
        _batteryAlerts.Remove(instanceId); // desligou (ou foi para a base): o alerta para
        if (Selected == vm) Selected = Controllers.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoControllers));
        OnPropertyChanged(nameof(ControllerCount));
        if (ShouldForward(vm.Info)) _ = UpdateHidingAsync();
    }

    private void Tick()
    {
        foreach (var c in Controllers)
        {
            var snap = _engine.GetLatest(c.Info.InstanceId);
            if (snap == null) continue;
            c.Apply(snap);
            CheckBattery(c);
        }
        if (++_tick % 30 == 0) RefreshVirtualStatus(); // status do controle virtual: 2x por segundo basta
        if (_tick % 6 == 0)
            MeasuredTurboText =
                _engine.DetectedControllerTurbo is { } d ? $"turbo do controle: {ButtonLabels.For(d.Button, Selected)} · {d.Hz:0.0} Hz"
                : _engine.MeasuredTurboHz is double hz ? $"medido agora: {hz:0.0} Hz"
                : "";
    }

    private void CheckBattery(ControllerViewModel c)
    {
        if (!c.IsWireless) return;
        var id = c.Info.InstanceId;
        if (!_batteryAlerts.TryGetValue(id, out var policy))
            _batteryAlerts[id] = policy = new BatteryAlertPolicy();

        if (policy.Update(c.Battery, DateTime.UtcNow) is int percent)
            LowBattery?.Invoke(c, percent);
    }

    public void Dispose()
    {
        _timer.Stop();
        Foreground.Dispose();
        _engine.Dispose();
        // Ao fechar o app, o controle físico volta a ficar visível para os jogos.
        if (_hidHide.Status == HidHideStatus.Hiding) _hidHide.Unhide();
    }
}
