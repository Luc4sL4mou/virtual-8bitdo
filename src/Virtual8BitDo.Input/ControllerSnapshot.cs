namespace Virtual8BitDo.Input;

/// <summary>Foto do estado do controle em um instante (leitura tempo real).</summary>
public sealed class ControllerSnapshot
{
    /// <summary>Todos os botões "crus" que o Windows expõe, na ordem do driver (índice 0..N-1).</summary>
    public required bool[] RawButtons { get; init; }
    public required short[] RawAxes { get; init; }
    public required byte[] RawHats { get; init; }

    /// <summary>Botões no padrão Xbox (ver StandardButtons). Só preenchido se o SDL reconhece o controle como gamepad.</summary>
    public required IReadOnlySet<string> PressedStandard { get; init; }

    // Valores normalizados: gatilhos 0..1, analógicos -1..1 (Y positivo = para baixo).
    public float LeftTrigger { get; init; }
    public float RightTrigger { get; init; }
    public float LeftX { get; init; }
    public float LeftY { get; init; }
    public float RightX { get; init; }
    public float RightY { get; init; }

    public BatteryStatus Battery { get; init; } = BatteryStatus.Unknown;
}
