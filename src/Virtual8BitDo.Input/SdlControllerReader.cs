using SDL;
using static SDL.SDL3;

namespace Virtual8BitDo.Input;

/// <summary>
/// Implementação baseada em SDL3. Abre cada dispositivo de duas formas:
///  - Joystick (cru): expõe TODOS os botões/eixos como o driver os enxerga;
///  - Gamepad (padrão Xbox): nomes consistentes (A, B, LB...) e os paddles (L4/R4/PL/PR), quando o SDL os reconhece.
/// O SDL3 tem um driver próprio para 8BitDo (HIDAPI) que lê os botões extras do Ultimate 2 via Bluetooth.
/// </summary>
public sealed unsafe class SdlControllerReader : IControllerReader
{
    private sealed class Device
    {
        public required ControllerInfo Info { get; init; }
        public required SDL_Joystick* Joystick { get; init; }
        public SDL_Gamepad* Gamepad { get; init; }

        // Bateria via XInput: consultada no máximo 1x por segundo (não precisa de 60x).
        public BatteryStatus XInputBattery = BatteryStatus.Unknown;
        public long XInputBatteryReadAt;

        // Bateria pelo canal de configuração da 8BitDo (modo Xbox: dongle ou cabo).
        public EightBitDoBatteryPoller? VendorBattery { get; init; }
    }

    private readonly Dictionary<int, Device> _devices = new();
    private bool _disposed;

    public event Action<ControllerInfo>? ControllerConnected;
    public event Action<int>? ControllerDisconnected;

    public IReadOnlyList<ControllerInfo> Controllers => _devices.Values.Select(d => d.Info).ToList();

    public SdlControllerReader()
    {
        // Continua lendo o controle mesmo com a janela sem foco.
        SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        // Driver 8BitDo do SDL (L4/R4/PL/PR). Já é o padrão; deixamos explícito.
        SDL_SetHint("SDL_JOYSTICK_HIDAPI_8BITDO", "1");

        if (!SDL_Init(SDL_InitFlags.SDL_INIT_JOYSTICK | SDL_InitFlags.SDL_INIT_GAMEPAD))
            throw new InvalidOperationException("Falha ao iniciar o SDL3: " + SDL_GetError());
    }

    public void Poll()
    {
        // Atualiza o estado de todos os controles (uma vez por ciclo; os GetSnapshot seguintes só leem).
        SDL_UpdateJoysticks();

        // Controles já conectados geram JOYSTICK_ADDED no primeiro Poll após o Init.
        SDL_Event e;
        while (SDL_PollEvent(&e))
        {
            switch ((SDL_EventType)e.type)
            {
                case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                    Open(e.jdevice.which);
                    break;
                case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                    Close((int)(uint)e.jdevice.which);
                    break;
            }
        }
    }

    private void Open(SDL_JoystickID id)
    {
        int instanceId = (int)(uint)id;
        if (_devices.ContainsKey(instanceId)) return;

        var joy = SDL_OpenJoystick(id);
        if (joy == null) return;

        var pad = SDL_IsGamepad(id) ? SDL_OpenGamepad(id) : null;

        string? path = SDL_GetJoystickPath(joy);
        var connection = DetectConnection(joy, path);

        // Fora do Bluetooth, o controle pode estar no modo Xbox 360 (XInput): aí o XInput diz se é cabo ou dongle.
        int? slot = null;
        string? diagnostic = null;
        if (connection != ConnectionType.Bluetooth && (slot = XInputInfo.FindSlot(SDL_GetJoystickPlayerIndex(joy))) is int s)
        {
            var viaXInput = XInputInfo.GetConnection(s);
            if (viaXInput != ConnectionType.Unknown) connection = viaXInput;
            diagnostic = XInputInfo.Describe(s);
        }

        var info = new ControllerInfo(
            instanceId,
            SDL_GetJoystickName(joy) ?? "Controle desconhecido",
            SDL_GetJoystickVendor(joy),
            SDL_GetJoystickProduct(joy),
            connection,
            pad != null,
            SDL_GetNumJoystickButtons(joy),
            SDL_GetNumJoystickAxes(joy),
            SDL_GetNumJoystickHats(joy),
            path)
        {
            XInputSlot = slot,
            Diagnostic = diagnostic,
        };

        _devices[instanceId] = new Device
        {
            Info = info, Joystick = joy, Gamepad = pad,
            // No Bluetooth o SDL já lê a bateria; no modo Xbox, perguntamos ao canal da 8BitDo.
            VendorBattery = connection == ConnectionType.Bluetooth ? null : EightBitDoBatteryPoller.TryCreate(info.Vid, info.Pid),
        };
        ControllerConnected?.Invoke(info);
    }

    private void Close(int instanceId)
    {
        if (!_devices.Remove(instanceId, out var d)) return;
        d.VendorBattery?.Dispose();
        if (d.Gamepad != null) SDL_CloseGamepad(d.Gamepad);
        SDL_CloseJoystick(d.Joystick);
        ControllerDisconnected?.Invoke(instanceId);
    }

    /// <summary>
    /// O caminho do dispositivo diz se é Bluetooth (clássico = serviço 1124, BLE = serviço 1812).
    /// Fora isso, o SDL diz se é com fio (cabo USB) ou sem fio (o que não for Bluetooth é o dongle 2.4G).
    /// Quando o SDL não sabe (comum no modo XInput), a UI completa pelo catálogo, que conhece o PID de cada modo.
    /// </summary>
    private static ConnectionType DetectConnection(SDL_Joystick* joy, string? path)
    {
        var p = path?.ToLowerInvariant() ?? "";
        if (p.Contains("bthenum") || p.Contains("bthledevice")
            || p.Contains("00001124-0000-1000-8000-00805f9b34fb")
            || p.Contains("00001812-0000-1000-8000-00805f9b34fb"))
            return ConnectionType.Bluetooth;

        return SDL_GetJoystickConnectionState(joy) switch
        {
            SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRED => ConnectionType.Usb,
            SDL_JoystickConnectionState.SDL_JOYSTICK_CONNECTION_WIRELESS => ConnectionType.Dongle24G,
            _ => ConnectionType.Unknown,
        };
    }

    private static BatteryStatus ReadBattery(SDL_Joystick* joy)
    {
        int percent;
        var state = SDL_GetJoystickPowerInfo(joy, &percent) switch
        {
            SDL_PowerState.SDL_POWERSTATE_ON_BATTERY => PowerState.OnBattery,
            SDL_PowerState.SDL_POWERSTATE_NO_BATTERY => PowerState.NoBattery,
            SDL_PowerState.SDL_POWERSTATE_CHARGING => PowerState.Charging,
            SDL_PowerState.SDL_POWERSTATE_CHARGED => PowerState.Charged,
            _ => PowerState.Unknown,
        };
        return new BatteryStatus(state, percent is >= 0 and <= 100 ? percent : null);
    }

    public ControllerSnapshot? GetSnapshot(int instanceId)
    {
        if (!_devices.TryGetValue(instanceId, out var d)) return null;

        var buttons = new bool[d.Info.ButtonCount];
        for (int i = 0; i < buttons.Length; i++)
            buttons[i] = SDL_GetJoystickButton(d.Joystick, i);

        var axes = new short[d.Info.AxisCount];
        for (int i = 0; i < axes.Length; i++)
            axes[i] = SDL_GetJoystickAxis(d.Joystick, i);

        var hats = new byte[d.Info.HatCount];
        for (int i = 0; i < hats.Length; i++)
            hats[i] = SDL_GetJoystickHat(d.Joystick, i);

        var pressed = new HashSet<string>();
        float lt = 0, rt = 0, lx = 0, ly = 0, rx = 0, ry = 0;

        if (d.Gamepad != null)
        {
            foreach (var (sdlButton, id) in ButtonMap)
                if (SDL_GetGamepadButton(d.Gamepad, sdlButton))
                    pressed.Add(id);

            lt = Trigger(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER));
            rt = Trigger(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER));
            lx = Stick(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX));
            ly = Stick(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY));
            rx = Stick(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX));
            ry = Stick(SDL_GetGamepadAxis(d.Gamepad, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY));
        }

        return new ControllerSnapshot
        {
            RawButtons = buttons,
            RawAxes = axes,
            RawHats = hats,
            PressedStandard = pressed,
            LeftTrigger = lt, RightTrigger = rt,
            LeftX = lx, LeftY = ly, RightX = rx, RightY = ry,
            Battery = ReadBattery(d),
        };
    }

    /// <summary>
    /// Bateria, na ordem: 1) SDL (Bluetooth, % exato); 2) canal da 8BitDo (modo Xbox, % exato);
    /// 3) XInput (só faixas, e o dongle 8BitDo nem informa).
    /// O canal da 8BitDo não diz se está carregando; quem sabe disso é a UI, que conhece o tipo de conexão.
    /// </summary>
    private static BatteryStatus ReadBattery(Device d)
    {
        var battery = ReadBattery(d.Joystick);
        if (battery.State != PowerState.Unknown) return battery;
        if (d.VendorBattery?.Percent is int vendorPercent) return new BatteryStatus(PowerState.OnBattery, vendorPercent);
        if (d.Info.XInputSlot is not int slot) return battery;

        long now = Environment.TickCount64;
        if (now - d.XInputBatteryReadAt >= 1000)
        {
            d.XInputBattery = XInputInfo.GetBattery(slot);
            d.XInputBatteryReadAt = now;
        }
        return d.XInputBattery;
    }

    public void Rumble(int instanceId, ushort lowFrequency, ushort highFrequency)
    {
        if (!_devices.TryGetValue(instanceId, out var d)) return;
        // Duração máxima do SDL (~65 s); o jogo manda 0 para parar, e cada novo pedido substitui o anterior.
        SDL_RumbleJoystick(d.Joystick, lowFrequency, highFrequency, 0xFFFF);
    }

    private static float Trigger(short v) => Math.Clamp(v / 32767f, 0f, 1f);
    private static float Stick(short v) => Math.Clamp(v / 32767f, -1f, 1f);

    private static readonly (SDL_GamepadButton, string)[] ButtonMap =
    {
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, StandardButtons.A),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, StandardButtons.B),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST, StandardButtons.X),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH, StandardButtons.Y),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, StandardButtons.LB),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, StandardButtons.RB),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK, StandardButtons.Back),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START, StandardButtons.Start),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE, StandardButtons.Guide),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK, StandardButtons.L3),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK, StandardButtons.R3),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, StandardButtons.DUp),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, StandardButtons.DDown),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, StandardButtons.DLeft),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, StandardButtons.DRight),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1, StandardButtons.P1),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1, StandardButtons.P2),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2, StandardButtons.P3),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2, StandardButtons.P4),
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _devices.Keys.ToList()) Close(id);
        SDL_Quit();
    }
}
