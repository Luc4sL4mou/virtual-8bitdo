using Virtual8BitDo.Input;

namespace Virtual8BitDo.Core;

/// <summary>
/// Decide quando avisar "bateria baixa" para UM controle. NÃ£o mostra nada: sÃ³ responde "avisar agora?".
///  - Ao chegar em 20% ou menos: avisa na hora.
///  - Enquanto continuar baixa: avisa de novo a cada 5 minutos, com o nÃ­vel atualizado.
///  - Se o nÃ­vel comeÃ§ar a subir (cabo ligado) ou o controle disser que estÃ¡ carregando: para de avisar.
///  - Se depois de subir voltar a cair (cabo tirado) ainda abaixo de 20%: volta a avisar.
///  - Acima de 20%: tudo volta ao normal.
/// Para evitar alarme falso, "subir" e "cair" sÃ³ contam com 2 pontos ou mais de diferenÃ§a (a leitura oscila).
/// </summary>
public sealed class BatteryAlertPolicy
{
    public const int Threshold = 20;
    public static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(5);
    private const int Hysteresis = 2;

    private enum Phase { Normal, Alerting, Recovering }

    private Phase _phase = Phase.Normal;
    private int _reference;          // Alerting: menor nÃ­vel visto. Recovering: maior nÃ­vel visto.
    private DateTime _lastAlert;

    /// <summary>Retorna o nÃ­vel a anunciar, ou null se nÃ£o for hora de avisar.</summary>
    public int? Update(BatteryStatus battery, DateTime now)
    {
        if (battery.Percent is not int p) return null;
        bool charging = battery.State is PowerState.Charging or PowerState.Charged or PowerState.NoBattery;

        if (p > Threshold)
        {
            _phase = Phase.Normal;
            return null;
        }

        switch (_phase)
        {
            case Phase.Normal:
                if (charging) { Enter(Phase.Recovering, p); return null; }
                Enter(Phase.Alerting, p);
                return Alert(p, now);

            case Phase.Alerting:
                _reference = Math.Min(_reference, p);
                if (charging || p >= _reference + Hysteresis) { Enter(Phase.Recovering, p); return null; }
                return now - _lastAlert >= RepeatInterval ? Alert(p, now) : null;

            default: // Recovering
                _reference = Math.Max(_reference, p);
                if (!charging && p <= _reference - Hysteresis) { Enter(Phase.Alerting, p); return Alert(p, now); }
                return null;
        }
    }

    private void Enter(Phase phase, int p) { _phase = phase; _reference = p; }

    private int Alert(int p, DateTime now) { _lastAlert = now; return p; }
}
