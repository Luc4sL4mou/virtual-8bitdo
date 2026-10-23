using System.Collections.ObjectModel;
using Virtual8BitDo.Catalog;
using Virtual8BitDo.Input;

namespace Virtual8BitDo.App.ViewModels;

public sealed class RawButtonViewModel : ObservableObject
{
    private bool _isPressed;
    public RawButtonViewModel(int index) => Index = index;
    public int Index { get; }
    public string Text => $"Btn {Index}";
    public bool IsPressed { get => _isPressed; set => Set(ref _isPressed, value); }
}

public sealed class AxisViewModel : ObservableObject
{
    private short _value;
    public AxisViewModel(int index) => Name = $"Eixo {index}";
    public string Name { get; }
    public short Value { get => _value; set { if (Set(ref _value, value)) OnPropertyChanged(nameof(Progress)); } }

    /// <summary>0..65535, para a ProgressBar.</summary>
    public double Progress => Value + 32768.0;
}

/// <summary>Um controle conectado: dados do dispositivo + modelo do catálogo + estado em tempo real.</summary>
public sealed class ControllerViewModel : ObservableObject
{
    private ControllerModel? _manualModel;
    private string _hatsText = "";
    private string _lastRawPressed = "(nenhum ainda)";

    public ControllerViewModel(ControllerInfo info, ControllerModel? knownModel, IReadOnlyList<ControllerModel> manualChoices)
    {
        Info = info;
        KnownModel = knownModel;
        ManualChoices = manualChoices;

        // O XInput não distingue cabo de dongle (o dongle se declara "com fio"); o número de série USB distingue.
        if (info.Connection != ConnectionType.Bluetooth && knownModel?.ReceiverPidValue is ushort receiverPid)
            (_usbProbeConnection, _usbProbeDetail) = UsbDeviceProbe.ClassifyReceiverLink(info.Vid, info.Pid, receiverPid);

        for (int i = 0; i < info.ButtonCount; i++) RawButtons.Add(new RawButtonViewModel(i));
        for (int i = 0; i < info.AxisCount; i++) Axes.Add(new AxisViewModel(i));
    }

    public ControllerInfo Info { get; }
    public ControllerModel? KnownModel { get; }
    public IReadOnlyList<ControllerModel> ManualChoices { get; }

    public ObservableCollection<RawButtonViewModel> RawButtons { get; } = new();
    public ObservableCollection<AxisViewModel> Axes { get; } = new();

    /// <summary>Nome do modelo, se conhecido; senão o nome que o Windows dá ao dispositivo.</summary>
    public string DisplayName => KnownModel?.Name ?? Info.Name;
    public string VidPidText => $"VID {Info.VidHex}  PID {Info.PidHex}";

    // Resultado da checagem USB (cabo x dongle com o mesmo PID), feita uma vez ao conectar.
    private readonly ConnectionType _usbProbeConnection = ConnectionType.Unknown;
    private readonly string? _usbProbeDetail;

    /// <summary>
    /// Ordem de confiança: 1) checagem USB pelo número de série (cabo x dongle);
    /// 2) o que o leitor detectou (Bluetooth / SDL / XInput); 3) o que o catálogo diz pelo VID/PID.
    /// </summary>
    public ConnectionType Connection =>
        _usbProbeConnection != ConnectionType.Unknown ? _usbProbeConnection
        : Info.Connection == ConnectionType.Unknown && Enum.TryParse<ConnectionType>(EffectiveModel?.Connection, out var c) ? c
        : Info.Connection;

    public string ConnectionMode => Connection switch
    {
        ConnectionType.Usb => "Cabo USB",
        ConnectionType.Dongle24G => "Sem fio 2.4G (dongle)",
        ConnectionType.Bluetooth => "Bluetooth",
        _ => "Desconhecido",
    };

    public bool IsWireless => Connection is ConnectionType.Dongle24G or ConnectionType.Bluetooth;

    /// <summary>Ícone (fonte Segoe Fluent Icons) do tipo de conexão: Bluetooth, sem fio 2.4G, cabo USB ou controle genérico.</summary>
    public string ConnectionGlyph => Connection switch
    {
        ConnectionType.Bluetooth => "",
        ConnectionType.Dongle24G => "",
        ConnectionType.Usb => "",
        _ => "",
    };

    // ---- Bateria (só exibida quando sem fio) ----
    private BatteryStatus _battery = BatteryStatus.Unknown;
    public BatteryStatus Battery => _battery;
    /// <summary>Sem fio: sempre (mesmo sem nível, para avisar que o modo não informa). No cabo: quando há nível para mostrar.</summary>
    public bool ShowBattery => IsWireless || _battery.Percent != null;

    /// <summary>0..1, para o preenchimento do ícone de bateria.</summary>
    public double BatteryFraction => (_battery.Percent ?? 0) / 100.0;

    public bool IsCharging => _battery.State is PowerState.Charging or PowerState.Charged;

    public string BatteryText => _battery.Percent is not int p ? "nível indisponível neste modo"
        : _battery.Approximate ? $"{ApproximateLevel(p)} (nível aproximado)"
        : _battery.State == PowerState.Charged ? $"{p}% · carregada"
        : IsCharging ? $"{p}% · carregando"
        : $"{p}%";

    /// <summary>Texto do alerta: "Bateria baixa = 18%" ou, quando o modo só informa faixas, "Bateria baixa (≈20%)".</summary>
    public string LowBatteryTitle(int percent) =>
        _battery.Approximate ? $"Bateria baixa (≈{percent}%)" : $"Bateria baixa = {percent}%";

    private static string ApproximateLevel(int p) => p switch
    {
        <= 5 => "quase vazia",
        <= 20 => "baixa",
        <= 60 => "média",
        _ => "cheia",
    };

    public string? DiagnosticText =>
        string.Join("\n", new[] { Info.Diagnostic, _usbProbeDetail }.Where(s => s != null)) is { Length: > 0 } t ? t : null;
    public bool HasDiagnostic => DiagnosticText != null;

    public System.Windows.Media.Brush BatteryBrush => new System.Windows.Media.SolidColorBrush(
        _battery.Percent is not int pct ? System.Windows.Media.Color.FromRgb(0x8A, 0x8A, 0x8A)
        : IsCharging ? System.Windows.Media.Color.FromRgb(0x60, 0xCD, 0xFF)
        : pct <= 20 ? System.Windows.Media.Color.FromRgb(0xFF, 0x6B, 0x5B)
        : pct <= 50 ? System.Windows.Media.Color.FromRgb(0xFC, 0xE1, 0x00)
        : System.Windows.Media.Color.FromRgb(0x6C, 0xCB, 0x5F));
    public string PathText => Info.DevicePath ?? "(indisponível: controle XInput)";
    public string GamepadSupportText => Info.IsGameController
        ? "Reconhecido como gamepad padrão (SDL)"
        : "Sem mapeamento de gamepad: só a lista de botões crus funciona";
    public string RawCountsText => $"{Info.ButtonCount} botões, {Info.AxisCount} eixos, {Info.HatCount} hats";

    public bool IsUnknown => KnownModel == null;
    public ControllerModel? EffectiveModel => KnownModel ?? ManualModel;

    public string ModelText =>
        KnownModel != null ? $"Windows chama de: {Info.Name}" :
        ManualModel != null ? $"{ManualModel.Name} (escolhido manualmente)" :
        "Modelo desconhecido";

    public string? ModelNote => EffectiveModel?.Note;
    public bool HasModelNote => !string.IsNullOrWhiteSpace(ModelNote);

    /// <summary>Escolha manual (só usada quando o VID/PID não está no catálogo).</summary>
    public ControllerModel? ManualModel
    {
        get => _manualModel;
        set
        {
            if (!Set(ref _manualModel, value)) return;
            OnPropertyChanged(nameof(EffectiveModel));
            OnPropertyChanged(nameof(ModelText));
            OnPropertyChanged(nameof(Connection));
            OnPropertyChanged(nameof(ConnectionMode));
            OnPropertyChanged(nameof(ConnectionGlyph));
            OnPropertyChanged(nameof(IsWireless));
            OnPropertyChanged(nameof(ShowBattery));
            OnPropertyChanged(nameof(ModelNote));
            OnPropertyChanged(nameof(HasModelNote));
            Updated?.Invoke();
        }
    }

    public string HatsText { get => _hatsText; private set => Set(ref _hatsText, value); }
    public string LastRawPressed { get => _lastRawPressed; private set => Set(ref _lastRawPressed, value); }

    // ---- Estado para o desenho ----
    public HashSet<string> PressedIds { get; } = new();
    public float LeftTrigger { get; private set; }
    public float RightTrigger { get; private set; }
    public float LeftX { get; private set; }
    public float LeftY { get; private set; }
    public float RightX { get; private set; }
    public float RightY { get; private set; }

    /// <summary>Disparado a cada atualização; o desenho se redesenha a partir dele.</summary>
    public event Action? Updated;

    /// <summary>O botão existe neste modelo? (cinza no desenho quando não.)</summary>
    public bool IsAvailable(string id)
    {
        var model = EffectiveModel;
        if (IsRearSlot(id)) return model != null && model.ExtrasReadable && model.ExtraButtons.Any(b => b.Id == id);
        return model == null || !model.MissingButtons.Contains(id);
    }

    /// <summary>Nome do botão traseiro neste modelo (PL, PR...), ou o próprio ID se não houver.</summary>
    public string ExtraLabel(string id) =>
        EffectiveModel?.ExtraButtons.FirstOrDefault(b => b.Id == id)?.Label ?? id;

    private static bool IsRearSlot(string id) => id.Length == 2 && id[0] == 'P' && char.IsDigit(id[1]);

    public void Apply(ControllerSnapshot s)
    {
        for (int i = 0; i < RawButtons.Count && i < s.RawButtons.Length; i++)
        {
            bool down = s.RawButtons[i];
            if (down && !RawButtons[i].IsPressed) LastRawPressed = RawButtons[i].Text;
            RawButtons[i].IsPressed = down;
        }
        for (int i = 0; i < Axes.Count && i < s.RawAxes.Length; i++)
            Axes[i].Value = s.RawAxes[i];
        HatsText = s.RawHats.Length == 0 ? "—" : string.Join("  ", s.RawHats.Select((h, i) => $"Hat {i}: {h}"));

        PressedIds.Clear();
        foreach (var id in s.PressedStandard) PressedIds.Add(id);

        // Botão traseiro que o SDL não mapeia: usa o índice cru definido no catálogo.
        if (EffectiveModel != null)
            foreach (var extra in EffectiveModel.ExtraButtons)
                if (extra.RawIndex is int idx && idx >= 0 && idx < s.RawButtons.Length && s.RawButtons[idx])
                    PressedIds.Add(extra.Id);

        // No cabo USB o controle está recebendo energia: "carregando" até 100%, depois "carregada".
        // (O canal da 8BitDo informa só o nível, não se está carregando.)
        var battery = s.Battery;
        if (Connection == ConnectionType.Usb && battery.Percent is int level && battery.State is PowerState.OnBattery or PowerState.Unknown)
            battery = battery with { State = level >= 100 ? PowerState.Charged : PowerState.Charging };

        if (battery != _battery)
        {
            _battery = battery;
            foreach (var name in new[] { nameof(Battery), nameof(BatteryFraction), nameof(IsCharging), nameof(BatteryText), nameof(BatteryBrush), nameof(ShowBattery) })
                OnPropertyChanged(name);
        }

        LeftTrigger = s.LeftTrigger; RightTrigger = s.RightTrigger;
        LeftX = s.LeftX; LeftY = s.LeftY; RightX = s.RightX; RightY = s.RightY;
        Updated?.Invoke();
    }
}
