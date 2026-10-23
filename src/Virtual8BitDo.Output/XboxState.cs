namespace Virtual8BitDo.Output;

/// <summary>Botões do Xbox 360 (mesmos bits do XInput).</summary>
[Flags]
public enum XboxButtons : ushort
{
    None = 0,
    DpadUp = 0x0001, DpadDown = 0x0002, DpadLeft = 0x0004, DpadRight = 0x0008,
    Start = 0x0010, Back = 0x0020,
    LeftThumb = 0x0040, RightThumb = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

/// <summary>
/// Estado completo que o controle virtual vai enviar ao jogo.
/// Analógicos: -32768..32767 com Y positivo = para CIMA (padrão XInput). Gatilhos: 0..255.
/// </summary>
public readonly record struct XboxState(
    XboxButtons Buttons,
    byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY);
