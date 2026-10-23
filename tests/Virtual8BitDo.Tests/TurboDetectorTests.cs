using Virtual8BitDo.Core.Mapping;
using Xunit;

namespace Virtual8BitDo.Tests;

/// <summary>Detecção do turbo que o próprio controle gera (botão apertado em ritmo constante).</summary>
public class TurboDetectorTests
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>();
    private static IReadOnlySet<string> Down(params string[] b) => new HashSet<string>(b);

    /// <summary>Simula o botão sendo apertado nos instantes dados (cada aperto dura 20 ms) e devolve a última detecção.</summary>
    private static TurboDetection? Simulate(TurboDetector d, string button, params long[] pressTimes)
    {
        TurboDetection? last = null;
        foreach (var t in pressTimes)
        {
            last = d.Update(Down(button), t);
            d.Update(None, t + 20);
        }
        return last;
    }

    [Fact]
    public void RegularPresses_AreDetectedWithTheirFrequency()
    {
        // 10 Hz: um aperto a cada 100 ms
        var r = Simulate(new TurboDetector(), "A", 0, 100, 200, 300, 400, 500);
        Assert.NotNull(r);
        Assert.Equal("A", r!.Value.Button);
        Assert.InRange(r.Value.Hz, 9.5, 10.5);
    }

    [Fact]
    public void FasterTurbo_ReportsHigherFrequency()
    {
        var r = Simulate(new TurboDetector(), "B", 0, 50, 100, 150, 200, 250, 300); // 20 Hz
        Assert.InRange(r!.Value.Hz, 19, 21);
    }

    [Fact]
    public void IrregularMashing_IsNotTurbo()
    {
        Assert.Null(Simulate(new TurboDetector(), "A", 0, 90, 340, 400, 580, 790));
    }

    [Fact]
    public void SlowPresses_AreNotTurbo()
    {
        Assert.Null(Simulate(new TurboDetector(), "A", 0, 600, 1200, 1800)); // 1,7 Hz
    }

    [Fact]
    public void TooFewPresses_AreNotTurbo()
    {
        Assert.Null(Simulate(new TurboDetector(), "A", 0, 100, 200));
    }

    [Fact]
    public void HoldingAButton_IsNotTurbo()
    {
        var d = new TurboDetector();
        TurboDetection? r = null;
        for (long t = 0; t < 2000; t += 5) r = d.Update(Down("A"), t);
        Assert.Null(r);
    }

    [Fact]
    public void TurboStops_WhenPressesStop()
    {
        var d = new TurboDetector();
        Simulate(d, "A", 0, 100, 200, 300, 400, 500);
        Assert.Null(d.Update(None, 900)); // 400 ms sem apertos: acabou
    }

    [Fact]
    public void ReportsTheButtonThatIsPulsing()
    {
        var d = new TurboDetector();
        d.Update(Down("X"), 0); d.Update(None, 20); // um aperto isolado em outro botão não atrapalha
        var r = Simulate(d, "A", 10, 110, 210, 310, 410);
        Assert.Equal("A", r!.Value.Button);
    }
}
