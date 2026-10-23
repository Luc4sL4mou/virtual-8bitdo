namespace Virtual8BitDo.Input;

/// <summary>Dados estáticos de um controle conectado.</summary>
public sealed record ControllerInfo(
    int InstanceId,
    string Name,
    ushort Vid,
    ushort Pid,
    ConnectionType Connection,
    bool IsGameController,
    int ButtonCount,
    int AxisCount,
    int HatCount,
    string? DevicePath)
{
    /// <summary>Slot XInput (0..3), quando o controle está no modo Xbox 360. Usado para ler bateria e conexão.</summary>
    public int? XInputSlot { get; init; }

    /// <summary>Detalhe técnico para diagnóstico (ex.: o que o XInput informou ao conectar).</summary>
    public string? Diagnostic { get; init; }

    public string VidHex => $"0x{Vid:X4}";
    public string PidHex => $"0x{Pid:X4}";
}
