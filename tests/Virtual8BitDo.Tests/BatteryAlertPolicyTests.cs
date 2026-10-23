using Virtual8BitDo.Core;
using Virtual8BitDo.Input;
using Xunit;

namespace Virtual8BitDo.Tests;

/// <summary>Regra do alerta de bateria baixa: 20%, repete a cada 5 min, para ao carregar.</summary>
public class BatteryAlertPolicyTests
{
    private static readonly DateTime T0 = new(2026, 1, 1);
    private const PowerState B = PowerState.OnBattery, C = PowerState.Charging;

    private static void Run(params (int Min, int Pct, PowerState State, int? Expect)[] steps)
    {
        var policy = new BatteryAlertPolicy();
        foreach (var (min, pct, state, expect) in steps)
            Assert.Equal(expect, policy.Update(new BatteryStatus(state, pct), T0.AddMinutes(min)));
    }

    [Fact] public void Discharge_AlertsAt20_ThenEvery5Minutes() =>
        Run((0, 25, B, null), (1, 21, B, null), (2, 20, B, 20), (3, 19, B, null), (6, 18, B, null), (7, 17, B, 17), (12, 15, B, 15));

    [Fact] public void SmallJitter_DoesNotStopAlerts() =>
        Run((0, 20, B, 20), (1, 19, B, null), (2, 20, B, null), (5, 19, B, 19));

    [Fact] public void LevelRising_StopsAlerts() =>
        Run((0, 18, B, 18), (1, 17, B, null), (2, 19, B, null), (10, 20, B, null), (20, 20, B, null));

    [Fact] public void ChargingState_StopsAlerts() =>
        Run((0, 18, B, 18), (1, 18, C, null), (10, 18, C, null));

    [Fact] public void CableRemovedAgain_ResumesAlerts() =>
        Run((0, 15, B, 15), (1, 15, C, null), (3, 19, C, null), (4, 19, B, null), (6, 17, B, 17), (11, 16, B, 16));

    [Fact] public void RechargedAbove20_ThenDropsAgain_AlertsAgain() =>
        Run((0, 20, B, 20), (1, 20, C, null), (30, 60, B, null), (300, 20, B, 20));

    [Fact]
    public void UnknownLevel_NeverAlerts() =>
        Assert.Null(new BatteryAlertPolicy().Update(new BatteryStatus(B, null), T0));
}
