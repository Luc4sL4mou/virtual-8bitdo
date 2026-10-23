using Virtual8BitDo.Core.Profiles;
using Virtual8BitDo.Input;
using Virtual8BitDo.Output;

namespace Virtual8BitDo.Core.Mapping;

/// <summary>Mudança feita pelo atalho no controle: nova frequência e, se for o caso, o botão cujo turbo mudou.</summary>
public readonly record struct TurboChange(int Hz, string? Button, bool? Enabled);

/// <summary>Resultado de um ciclo: o que vai para o Xbox virtual e quais teclas devem estar apertadas.</summary>
public readonly record struct MapResult(XboxState Xbox, IReadOnlySet<ushort> Keys);

/// <summary>
/// Aplica um perfil ao estado de UM controle físico: remapeamento, teclas, turbo e o atalho de turbo no controle.
/// Roda na thread do motor (~1000x por segundo). Altera o perfil recebido quando o atalho é usado.
///
/// Atalho no controle (segurando o botão Home/Guide):
///   Home + D-pad ↑/↓   → frequência do turbo +1/−1 Hz      Home + D-pad →/← → +5/−5 Hz
///   Home + outro botão → liga/desliga o turbo daquele botão
///   Home sozinho       → funciona como Home normal (um toque curto ao soltar)
/// </summary>
public sealed class Mapper
{
    private const float TriggerThreshold = 0.3f;
    private const long GuideTapMs = 80;

    private static readonly Dictionary<string, XboxButtons> Flags = new()
    {
        [StandardButtons.A] = XboxButtons.A, [StandardButtons.B] = XboxButtons.B,
        [StandardButtons.X] = XboxButtons.X, [StandardButtons.Y] = XboxButtons.Y,
        [StandardButtons.LB] = XboxButtons.LeftShoulder, [StandardButtons.RB] = XboxButtons.RightShoulder,
        [StandardButtons.Back] = XboxButtons.Back, [StandardButtons.Start] = XboxButtons.Start,
        [StandardButtons.Guide] = XboxButtons.Guide,
        [StandardButtons.L3] = XboxButtons.LeftThumb, [StandardButtons.R3] = XboxButtons.RightThumb,
        [StandardButtons.DUp] = XboxButtons.DpadUp, [StandardButtons.DDown] = XboxButtons.DpadDown,
        [StandardButtons.DLeft] = XboxButtons.DpadLeft, [StandardButtons.DRight] = XboxButtons.DpadRight,
    };

    private readonly HashSet<string> _prevDown = new();
    private readonly HashSet<string> _consumed = new();        // apertados durante o atalho: ignorados até soltar
    private readonly Dictionary<string, long> _turboStart = new();
    private readonly Dictionary<string, bool> _turboWasOn = new();
    private readonly Dictionary<string, Queue<long>> _turboEdges = new();
    private bool _layer, _layerUsed;
    private long _guideTapUntil;

    /// <summary>O atalho no controle mudou o turbo (disparado na thread do motor).</summary>
    public event Action<TurboChange>? TurboChanged;

    /// <summary>Frequência real medida do turbo (pulsos no último segundo), ou null se nenhum botão com turbo está segurado.</summary>
    public double? MeasuredTurboHz { get; private set; }

    public MapResult Map(ControllerSnapshot s, Profile profile, long nowMs)
    {
        var down = ReadSources(s);
        HandleShortcutLayer(down, profile, nowMs);

        var buttons = XboxButtons.None;
        byte lt = 0, rt = 0;
        var keys = new HashSet<ushort>();
        bool anyTurbo = false;

        foreach (var src in Profile.Sources)
        {
            bool pressed = IsEffectivelyDown(src, down, nowMs);
            var action = profile.Get(src);

            bool on = pressed;
            if (pressed && action.Turbo)
            {
                on = TurboPhaseOn(src, profile.TurboHz, nowMs);
                anyTurbo = true;
            }
            else
            {
                _turboStart.Remove(src);
                _turboWasOn.Remove(src);
            }

            switch (action.Kind)
            {
                case ActionKind.Default when Profile.IsExtra(src):
                case ActionKind.None:
                    break;

                case ActionKind.Default when src == "LT":
                    lt = action.Turbo ? (on ? Byte(s.LeftTrigger) : (byte)0) : Byte(s.LeftTrigger); // analógico preservado
                    break;
                case ActionKind.Default when src == "RT":
                    rt = action.Turbo ? (on ? Byte(s.RightTrigger) : (byte)0) : Byte(s.RightTrigger);
                    break;
                case ActionKind.Default:
                    if (on && Flags.TryGetValue(src, out var f)) buttons |= f;
                    break;

                case ActionKind.Xbox when on:
                    foreach (var target in action.Xbox)
                    {
                        if (target == "LT") lt = 255;
                        else if (target == "RT") rt = 255;
                        else if (Flags.TryGetValue(target, out var tf)) buttons |= tf;
                    }
                    break;

                case ActionKind.Key when on:
                    if (action.Key != null && KeyboardOutput.Keys.TryGetValue(action.Key, out var vk)) keys.Add(vk);
                    break;
            }
        }

        MeasuredTurboHz = anyTurbo ? MeasureTurbo(nowMs) : null;

        var xbox = new XboxState(buttons, lt, rt,
            Axis(s.LeftX), Axis(-s.LeftY),   // SDL: Y positivo = baixo; XInput: Y positivo = cima
            Axis(s.RightX), Axis(-s.RightY));
        return new MapResult(xbox, keys);
    }

    private static HashSet<string> ReadSources(ControllerSnapshot s)
    {
        var down = new HashSet<string>(s.PressedStandard);
        if (s.LeftTrigger > TriggerThreshold) down.Add("LT");
        if (s.RightTrigger > TriggerThreshold) down.Add("RT");
        return down;
    }

    /// <summary>Atalho de turbo: enquanto o Home está segurado, os botões viram comandos e não vão para o jogo.</summary>
    private void HandleShortcutLayer(HashSet<string> down, Profile profile, long now)
    {
        bool guide = down.Contains(StandardButtons.Guide);
        if (guide && !_layer) { _layer = true; _layerUsed = false; }

        if (_layer)
        {
            foreach (var src in down)
            {
                if (src == StandardButtons.Guide || _prevDown.Contains(src)) continue; // só no momento em que aperta
                _layerUsed = true;
                _consumed.Add(src);

                int step = src switch
                {
                    StandardButtons.DUp => 1, StandardButtons.DDown => -1,
                    StandardButtons.DRight => 5, StandardButtons.DLeft => -5,
                    _ => 0,
                };
                if (step != 0)
                {
                    profile.TurboHz = Math.Clamp(profile.TurboHz + step, Profile.MinTurboHz, Profile.MaxTurboHz);
                    TurboChanged?.Invoke(new TurboChange(profile.TurboHz, null, null));
                }
                else
                {
                    var action = profile.Get(src).Clone();
                    action.Turbo = !action.Turbo;
                    profile.Set(src, action);
                    TurboChanged?.Invoke(new TurboChange(profile.TurboHz, src, action.Turbo));
                }
            }

            if (!guide)
            {
                _layer = false;
                if (!_layerUsed) _guideTapUntil = now + GuideTapMs; // Home sozinho: vira um toque normal
            }
        }

        _consumed.RemoveWhere(src => !down.Contains(src));
        _prevDown.Clear();
        _prevDown.UnionWith(down);
    }

    private bool IsEffectivelyDown(string src, HashSet<string> down, long now)
    {
        if (src == StandardButtons.Guide) return now < _guideTapUntil; // o Home real só sai como toque
        if (_layer || _consumed.Contains(src)) return false;
        return down.Contains(src);
    }

    /// <summary>Turbo: começa "apertado" e alterna a cada meio período.</summary>
    private bool TurboPhaseOn(string src, int hz, long now)
    {
        if (!_turboStart.TryGetValue(src, out var start)) _turboStart[src] = start = now;
        double period = 1000.0 / Math.Clamp(hz, Profile.MinTurboHz, Profile.MaxTurboHz);
        bool on = (now - start) % period < period / 2;

        // Conta cada "aperto" gerado, para medir a frequência real.
        if (on && !_turboWasOn.GetValueOrDefault(src))
        {
            if (!_turboEdges.TryGetValue(src, out var q)) _turboEdges[src] = q = new Queue<long>();
            q.Enqueue(now);
        }
        _turboWasOn[src] = on;
        return on;
    }

    private double? MeasureTurbo(long now)
    {
        // Frequência = pulsos ÷ tempo entre o primeiro e o último (janela de 1 s): fica certa já no começo.
        double? best = null;
        foreach (var q in _turboEdges.Values)
        {
            while (q.Count > 0 && now - q.Peek() > 1000) q.Dequeue();
            if (q.Count < 2) continue;
            double hz = (q.Count - 1) * 1000.0 / (q.Last() - q.Peek());
            if (best == null || hz > best) best = hz;
        }
        return best;
    }

    private static byte Byte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
    private static short Axis(float v) => (short)Math.Clamp(Math.Round(v * 32767f), short.MinValue, short.MaxValue);
}
