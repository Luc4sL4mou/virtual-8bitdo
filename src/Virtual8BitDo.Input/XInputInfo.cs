using System.Runtime.InteropServices;

namespace Virtual8BitDo.Input;

/// <summary>
/// Leitura direta do XInput (a API do Xbox no Windows). Usada quando o controle está no modo Xbox 360
/// (dongle 2.4G ou cabo), em que o SDL não informa bateria nem tipo de conexão.
/// O XInput diz se o controle é "com fio" ou "na bateria", e o nível em 4 faixas (não em %).
/// </summary>
internal static class XInputInfo
{
    private const byte BatteryDevTypeGamepad = 0;
    private const byte TypeDisconnected = 0, TypeWired = 1, TypeAlkaline = 2, TypeNimh = 3, TypeUnknown = 0xFF;

    [StructLayout(LayoutKind.Sequential)]
    private struct BatteryInformation { public byte BatteryType; public byte BatteryLevel; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Capabilities
    {
        public byte Type, SubType; public ushort Flags;
        public ushort Buttons; public byte LeftTrigger, RightTrigger; public short LX, LY, RX, RY;
        public ushort LeftMotor, RightMotor;
    }

    [DllImport("xinput1_4.dll")] private static extern int XInputGetBatteryInformation(int userIndex, byte devType, out BatteryInformation info);
    [DllImport("xinput1_4.dll")] private static extern int XInputGetCapabilities(int userIndex, int flags, out Capabilities caps);

    private static bool IsConnected(int slot)
    {
        try { return XInputGetCapabilities(slot, 0, out _) == 0; }
        catch (DllNotFoundException) { return false; }
    }

    /// <summary>
    /// Descobre o slot XInput (0..3) do controle. O SDL costuma usar o slot como "player index";
    /// se não bater, e houver só um controle XInput ligado, usa esse.
    /// </summary>
    public static int? FindSlot(int sdlPlayerIndex)
    {
        if (sdlPlayerIndex is >= 0 and <= 3 && IsConnected(sdlPlayerIndex)) return sdlPlayerIndex;
        var connected = Enumerable.Range(0, 4).Where(IsConnected).ToList();
        return connected.Count == 1 ? connected[0] : null;
    }

    /// <summary>Tipo de conexão pelo XInput: "com fio" = cabo USB; com bateria = sem fio (dongle 2.4G).</summary>
    public static ConnectionType GetConnection(int slot) => ReadRaw(slot)?.BatteryType switch
    {
        TypeWired => ConnectionType.Usb,
        TypeAlkaline or TypeNimh or TypeUnknown => ConnectionType.Dongle24G,
        _ => ConnectionType.Unknown,
    };

    /// <summary>Bateria pelo XInput. O nível vem em faixas; convertemos para um % aproximado.</summary>
    public static BatteryStatus GetBattery(int slot)
    {
        if (ReadRaw(slot) is not { } b) return BatteryStatus.Unknown;
        return b.BatteryType switch
        {
            TypeWired => new BatteryStatus(PowerState.NoBattery, null),
            TypeAlkaline or TypeNimh => new BatteryStatus(PowerState.OnBattery, b.BatteryLevel switch
            {
                0 => 5,    // EMPTY
                1 => 20,   // LOW
                2 => 60,   // MEDIUM
                _ => 100,  // FULL
            }, Approximate: true),
            _ => BatteryStatus.Unknown,
        };
    }

    /// <summary>Texto para diagnóstico na tela (ex.: "XInput slot 0 · bateria NiMH · nível 2/3").</summary>
    public static string Describe(int slot)
    {
        if (ReadRaw(slot) is not { } b) return $"XInput slot {slot} · sem informação de bateria";
        string type = b.BatteryType switch
        {
            TypeWired => "com fio", TypeAlkaline => "pilha alcalina", TypeNimh => "bateria NiMH",
            TypeUnknown => "bateria de tipo desconhecido", TypeDisconnected => "desconectado", _ => $"tipo {b.BatteryType}",
        };
        return $"XInput slot {slot} · {type} · nível {b.BatteryLevel}/3";
    }

    private static BatteryInformation? ReadRaw(int slot)
    {
        try { return XInputGetBatteryInformation(slot, BatteryDevTypeGamepad, out var b) == 0 ? b : null; }
        catch (DllNotFoundException) { return null; }
    }
}
