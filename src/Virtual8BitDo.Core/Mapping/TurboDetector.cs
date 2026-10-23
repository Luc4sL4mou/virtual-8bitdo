namespace Virtual8BitDo.Core.Mapping;

/// <summary>Turbo gerado pelo próprio controle: o botão e a frequência medida.</summary>
public readonly record struct TurboDetection(string Button, double Hz);

/// <summary>
/// Detecta o turbo que o PRÓPRIO controle gera (Estrela + botão, no 8BitDo). O PC não recebe a Estrela, mas vê
/// o botão sendo apertado e solto em ritmo constante. Um humano apertando rápido é irregular; o turbo do controle,
/// regular. Então: ao menos 4 apertos na última 1 s, todos com intervalo entre 40 e 400 ms e variação pequena
/// (desvio ≤ 30% da média) contam como turbo. A frequência é medida pelo tempo entre o primeiro e o último aperto.
/// Use sempre com o estado FÍSICO do controle (antes do mapeamento), para não "detectar" o turbo do próprio app.
/// </summary>
public sealed class TurboDetector
{
    private const long WindowMs = 1000;
    private const int MinPresses = 4;
    private const long MinGapMs = 40, MaxGapMs = 400;
    private const double MaxVariation = 0.30;

    private readonly Dictionary<string, List<long>> _presses = new();
    private readonly HashSet<string> _prev = new();

    /// <summary>Informa o que está apertado agora. Retorna o turbo detectado (o mais forte), ou null.</summary>
    public TurboDetection? Update(IReadOnlySet<string> down, long nowMs)
    {
        foreach (var button in down)
        {
            if (_prev.Contains(button)) continue; // só conta o momento em que aperta
            if (!_presses.TryGetValue(button, out var list)) _presses[button] = list = new List<long>();
            list.Add(nowMs);
        }
        _prev.Clear();
        _prev.UnionWith(down);

        TurboDetection? best = null;
        int bestCount = 0;
        foreach (var (button, list) in _presses.ToList())
        {
            list.RemoveAll(t => nowMs - t > WindowMs);
            if (list.Count == 0) { _presses.Remove(button); continue; }
            if (!LooksLikeTurbo(list, nowMs, out double hz) || list.Count <= bestCount) continue;
            best = new TurboDetection(button, hz);
            bestCount = list.Count;
        }
        return best;
    }

    private static bool LooksLikeTurbo(List<long> presses, long now, out double hz)
    {
        hz = 0;
        if (presses.Count < MinPresses) return false;

        var gaps = new double[presses.Count - 1];
        for (int i = 1; i < presses.Count; i++) gaps[i - 1] = presses[i] - presses[i - 1];
        if (gaps.Any(g => g < MinGapMs || g > MaxGapMs)) return false;

        double mean = gaps.Average();
        double deviation = Math.Sqrt(gaps.Average(g => (g - mean) * (g - mean)));
        if (deviation / mean > MaxVariation) return false;

        // Já parou? Se o último aperto ficou para trás por mais de 2 intervalos, o turbo acabou.
        if (now - presses[^1] > mean * 2.2) return false;

        hz = (presses.Count - 1) * 1000.0 / (presses[^1] - presses[0]);
        return true;
    }
}
