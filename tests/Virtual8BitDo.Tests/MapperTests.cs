using Virtual8BitDo.Core.Mapping;
using Virtual8BitDo.Core.Profiles;
using Virtual8BitDo.Input;
using Virtual8BitDo.Output;
using Xunit;

namespace Virtual8BitDo.Tests;

/// <summary>Mapeamento, teclas, turbo e atalho no controle (etapas 4 e 7).</summary>
public class MapperTests
{
    private static ControllerSnapshot S(params string[] pressed) => S(0f, pressed);

    private static ControllerSnapshot S(float leftTrigger, params string[] pressed) => new()
    {
        RawButtons = Array.Empty<bool>(), RawAxes = Array.Empty<short>(), RawHats = Array.Empty<byte>(),
        PressedStandard = new HashSet<string>(pressed), LeftTrigger = leftTrigger,
    };

    [Fact]
    public void EmptyProfile_IsOneToOne_AndExtrasDoNothing()
    {
        var m = new Mapper();
        var r = m.Map(S("A", "P1"), new Profile(), 0);
        Assert.Equal(XboxButtons.A, r.Xbox.Buttons);
        Assert.Empty(r.Keys);
    }

    [Fact]
    public void Trigger_KeepsAnalogValue()
    {
        Assert.Equal(128, new Mapper().Map(S(0.5f), new Profile(), 0).Xbox.LeftTrigger);
    }

    [Fact]
    public void Extra_ToKey_ToCombo_AndRemaps()
    {
        var p = new Profile();
        p.Set("P1", new ButtonAction { Kind = ActionKind.Key, Key = "F13" });
        p.Set("P2", new ButtonAction { Kind = ActionKind.Xbox, Xbox = { "LB", "A" } });
        p.Set("A", new ButtonAction { Kind = ActionKind.None });
        p.Set("B", new ButtonAction { Kind = ActionKind.Xbox, Xbox = { "Y" } });
        var m = new Mapper();

        Assert.Equal(new ushort[] { 0x7C }, m.Map(S("P1"), p, 0).Keys); // F13
        Assert.Equal(XboxButtons.LeftShoulder | XboxButtons.A, m.Map(S("P2"), p, 1).Xbox.Buttons);
        Assert.Equal(XboxButtons.None, m.Map(S("A"), p, 2).Xbox.Buttons);
        Assert.Equal(XboxButtons.Y, m.Map(S("B"), p, 3).Xbox.Buttons);
    }

    [Fact]
    public void Turbo_PulsesAtProfileFrequency_AndMeasuresIt()
    {
        var p = new Profile { TurboHz = 10 };
        p.Set("A", new ButtonAction { Turbo = true });
        var m = new Mapper();

        Assert.Equal(XboxButtons.A, m.Map(S("A"), p, 0).Xbox.Buttons);     // aperta já no início
        Assert.Equal(XboxButtons.None, m.Map(S("A"), p, 60).Xbox.Buttons); // solta no meio período
        Assert.Equal(XboxButtons.A, m.Map(S("A"), p, 110).Xbox.Buttons);  // aperta de novo
        for (long t = 111; t <= 1000; t++) m.Map(S("A"), p, t);
        Assert.InRange(m.MeasuredTurboHz!.Value, 9.5, 10.5);

        m.Map(S(), p, 1001);
        Assert.Null(m.MeasuredTurboHz);
    }

    [Fact]
    public void HomeShortcut_ChangesFrequency_AndSwallowsButtons()
    {
        var p = new Profile { TurboHz = 10 };
        var m = new Mapper();
        var changes = new List<TurboChange>();
        m.TurboChanged += changes.Add;

        Assert.Equal(XboxButtons.None, m.Map(S("Guide"), p, 0).Xbox.Buttons);
        Assert.Equal(XboxButtons.None, m.Map(S("Guide", "DUp"), p, 10).Xbox.Buttons);
        Assert.Equal(11, p.TurboHz);
        m.Map(S("Guide", "DUp"), p, 20);                       // segurar não repete
        Assert.Equal(11, p.TurboHz);
        m.Map(S("Guide", "DRight"), p, 30);
        Assert.Equal(16, p.TurboHz);
        Assert.Equal(XboxButtons.None, m.Map(S("DRight"), p, 40).Xbox.Buttons); // continua ignorado até soltar
        Assert.Equal(XboxButtons.None, m.Map(S(), p, 50).Xbox.Buttons);         // Home usado: sem toque
        Assert.Equal(XboxButtons.DpadRight, m.Map(S("DRight"), p, 60).Xbox.Buttons);
        Assert.Equal(new[] { 11, 16 }, changes.Select(c => c.Hz));
    }

    [Fact]
    public void HomeShortcut_FrequencyIsClamped()
    {
        var p = new Profile { TurboHz = 28 };
        var m = new Mapper();
        for (int i = 0; i < 5; i++) { m.Map(S("Guide"), p, i * 2); m.Map(S("Guide", "DRight"), p, i * 2 + 1); }
        Assert.Equal(Profile.MaxTurboHz, p.TurboHz);
    }

    [Fact]
    public void HomePlusButton_TogglesTurbo()
    {
        var p = new Profile();
        var m = new Mapper();
        m.Map(S("Guide"), p, 0); m.Map(S("Guide", "A"), p, 5); m.Map(S(), p, 10);
        Assert.True(p.Get("A").Turbo);
        m.Map(S("Guide"), p, 20); m.Map(S("Guide", "A"), p, 25); m.Map(S(), p, 30);
        Assert.False(p.Get("A").Turbo);
    }

    [Fact]
    public void HomeAlone_BecomesShortTap()
    {
        var p = new Profile();
        var m = new Mapper();
        m.Map(S("Guide"), p, 0);
        Assert.Equal(XboxButtons.Guide, m.Map(S(), p, 100).Xbox.Buttons & XboxButtons.Guide);
        Assert.Equal(XboxButtons.None, m.Map(S(), p, 200).Xbox.Buttons & XboxButtons.Guide);
    }
}
