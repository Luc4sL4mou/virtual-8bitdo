using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace Virtual8BitDo.Output;

/// <summary>Um controle virtual Xbox 360 (via ViGEmBus). Para o jogo, é um controle Xbox de verdade.</summary>
public sealed class VirtualXboxPad : IDisposable
{
    private static readonly (XboxButtons Flag, Xbox360Button Button)[] Buttons =
    {
        (XboxButtons.DpadUp, Xbox360Button.Up), (XboxButtons.DpadDown, Xbox360Button.Down),
        (XboxButtons.DpadLeft, Xbox360Button.Left), (XboxButtons.DpadRight, Xbox360Button.Right),
        (XboxButtons.Start, Xbox360Button.Start), (XboxButtons.Back, Xbox360Button.Back),
        (XboxButtons.LeftThumb, Xbox360Button.LeftThumb), (XboxButtons.RightThumb, Xbox360Button.RightThumb),
        (XboxButtons.LeftShoulder, Xbox360Button.LeftShoulder), (XboxButtons.RightShoulder, Xbox360Button.RightShoulder),
        (XboxButtons.Guide, Xbox360Button.Guide),
        (XboxButtons.A, Xbox360Button.A), (XboxButtons.B, Xbox360Button.B),
        (XboxButtons.X, Xbox360Button.X), (XboxButtons.Y, Xbox360Button.Y),
    };

    private readonly IXbox360Controller _pad;
    private XboxState? _last;

    /// <summary>O jogo pediu vibração (motor grande, motor pequeno: 0..255). Vem de uma thread do driver.</summary>
    public event Action<byte, byte>? Rumble;

    internal VirtualXboxPad(ViGEmClient client)
    {
        _pad = client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false; // montamos o estado inteiro e enviamos de uma vez
        _pad.FeedbackReceived += (_, e) => Rumble?.Invoke(e.LargeMotor, e.SmallMotor);
        _pad.Connect();
    }

    /// <summary>Envia o estado ao jogo. Só fala com o driver quando algo mudou.</summary>
    public void Send(in XboxState state)
    {
        if (_last == state) return;
        _last = state;

        foreach (var (flag, button) in Buttons)
            _pad.SetButtonState(button, (state.Buttons & flag) != 0);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, state.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, state.RightTrigger);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, state.LeftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, state.LeftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, state.RightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, state.RightY);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        try { _pad.Disconnect(); } catch (Exception) { /* driver já removeu */ }
    }
}
