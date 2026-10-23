using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Virtual8BitDo.Core.Mapping;
using Virtual8BitDo.Core.Profiles;
using Virtual8BitDo.Input;
using Virtual8BitDo.Output;

namespace Virtual8BitDo.Core;

/// <summary>
/// O "motor": uma thread dedicada que lê o controle físico ~1000x por segundo, aplica o perfil ativo
/// (mapeamento, teclas, turbo) e envia o resultado ao controle virtual e ao teclado imediatamente.
/// A tela não participa desse caminho: ela só consulta o último estado (<see cref="GetLatest"/>) no ritmo dela.
/// Os eventos são disparados NA THREAD DO MOTOR; quem for mexer em tela deve repassar para a thread da UI.
/// </summary>
public sealed class ControllerEngine : IDisposable
{
    private readonly Func<IControllerReader> _readerFactory;
    private readonly Func<ControllerInfo, bool> _shouldForward;
    private readonly ConcurrentDictionary<int, ControllerSnapshot> _latest = new();
    private readonly ConcurrentQueue<(int Id, ushort Low, ushort High)> _rumble = new();
    private readonly ManualResetEventSlim _started = new();
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private VirtualPadManager? _pads;
    private Exception? _startError;
    private volatile bool _stop;
    private volatile bool _forwarding = true;
    private volatile Profile? _pendingProfile;
    private volatile string _activeProfileName = "Padrão";
    private double _measuredTurboHz = double.NaN;

    public event Action<ControllerInfo>? ControllerConnected;
    public event Action<int>? ControllerDisconnected;

    /// <summary>O atalho no controle mudou o turbo (nova frequência / botão ligado ou desligado).</summary>
    public event Action<TurboChange>? TurboChanged;

    /// <summary>O próprio controle está gerando turbo (Estrela + botão): primeira detecção ou frequência que mudou.</summary>
    public event Action<TurboDetection>? ControllerTurboDetected;

    // Caixa de referência: assim a thread da tela lê o valor inteiro de uma vez (sem ler Botão de um e Hz de outro).
    private sealed class DetectionBox { public required TurboDetection Value { get; init; } }
    private volatile DetectionBox? _detectedTurbo;

    /// <summary>Turbo que o controle está gerando agora, medido ao vivo. Null quando não há.</summary>
    public TurboDetection? DetectedControllerTurbo => _detectedTurbo?.Value;

    /// <param name="readerFactory">Cria o leitor (precisa ser criado na própria thread do motor, por causa do SDL).</param>
    /// <param name="shouldForward">Quais controles físicos ganham um controle virtual.</param>
    public ControllerEngine(Func<IControllerReader> readerFactory, Func<ControllerInfo, bool> shouldForward)
    {
        _readerFactory = readerFactory;
        _shouldForward = shouldForward;
        _thread = new Thread(Run) { Name = "Virtual8BitDo.Engine", IsBackground = true, Priority = ThreadPriority.AboveNormal };
    }

    /// <summary>Inicia o motor e espera o leitor ficar pronto. Lança a exceção se o leitor falhar ao iniciar.</summary>
    public void Start()
    {
        _thread.Start();
        _started.Wait();
        if (_startError != null) throw _startError;
    }

    /// <summary>Liga/desliga o repasse para o controle virtual. Desligado, os controles virtuais são removidos.</summary>
    public bool Forwarding { get => _forwarding; set => _forwarding = value; }

    /// <summary>Troca o perfil em uso. O motor guarda uma cópia própria (o atalho de turbo altera essa cópia).</summary>
    public void SetProfile(Profile profile)
    {
        _activeProfileName = profile.Name;
        _pendingProfile = profile.Clone();
    }

    public string ActiveProfileName => _activeProfileName;

    /// <summary>Frequência real do turbo agora (Hz), ou null se nenhum botão com turbo está segurado.</summary>
    public double? MeasuredTurboHz
    {
        get { var v = Volatile.Read(ref _measuredTurboHz); return double.IsNaN(v) ? null : v; }
    }

    public VirtualBusStatus BusStatus => _pads?.Status ?? VirtualBusStatus.Error;
    public string? BusError => _pads?.Error;

    /// <summary>Quantos controles virtuais estão ativos.</summary>
    public int VirtualPadCount => _pads?.Count ?? 0;

    /// <summary>Último estado lido do controle (para a tela). Seguro de qualquer thread.</summary>
    public ControllerSnapshot? GetLatest(int instanceId) => _latest.TryGetValue(instanceId, out var s) ? s : null;

    /// <summary>Último erro ocorrido dentro do motor (diagnóstico).</summary>
    public string? LastError { get; private set; }

    private void Run()
    {
        IControllerReader reader;
        var controllers = new Dictionary<int, ControllerInfo>();
        var mappers = new Dictionary<int, Mapper>();
        var detectors = new Dictionary<int, TurboDetector>();
        TurboDetection? lastReported = null;
        var keysDown = new HashSet<ushort>();
        var profile = new Profile();
        try
        {
            reader = _readerFactory();
            _pads = new VirtualPadManager();
        }
        catch (Exception ex)
        {
            _startError = ex;
            _started.Set();
            return;
        }

        reader.ControllerConnected += info =>
        {
            controllers[info.InstanceId] = info;
            ControllerConnected?.Invoke(info);
        };
        reader.ControllerDisconnected += id =>
        {
            controllers.Remove(id);
            mappers.Remove(id);
            detectors.Remove(id);
            _latest.TryRemove(id, out _);
            _pads.Remove(id);
            ControllerDisconnected?.Invoke(id);
        };
        _started.Set();

        // Sem isso, Thread.Sleep(1) no Windows dorme ~15 ms; com isso, ~1 ms.
        timeBeginPeriod(1);
        try
        {
            while (!_stop)
            {
                try { Cycle(); }
                catch (Exception ex) { LastError = ex.Message; } // um ciclo com erro não derruba o motor
                Thread.Sleep(1);
            }
        }
        finally
        {
            ReleaseKeys();
            timeEndPeriod(1);
            _pads.Dispose();
            reader.Dispose();
        }

        void Cycle()
        {
            if (Interlocked.Exchange(ref _pendingProfile, null) is { } newProfile)
            {
                profile = newProfile;
                ReleaseKeys(); // nenhuma tecla do perfil anterior pode ficar "presa"
            }

            reader.Poll();
            bool forwarding = _forwarding;
            long now = _clock.ElapsedMilliseconds;
            var wantedKeys = new HashSet<ushort>();
            double measured = double.NaN;
            TurboDetection? detected = null;

            foreach (var (id, info) in controllers)
            {
                var snap = reader.GetSnapshot(id);
                if (snap == null) continue;
                _latest[id] = snap;

                // Turbo gerado pelo próprio controle: medido no estado físico (antes do mapeamento do app).
                if (!detectors.TryGetValue(id, out var detector)) detectors[id] = detector = new TurboDetector();
                if (detector.Update(PhysicalDown(snap), now) is { } found && (detected == null || found.Hz > detected.Value.Hz))
                    detected = found;

                if (!forwarding || !_shouldForward(info)) continue;

                if (!mappers.TryGetValue(id, out var mapper))
                {
                    mappers[id] = mapper = new Mapper();
                    mapper.TurboChanged += change => TurboChanged?.Invoke(change);
                }

                var result = mapper.Map(snap, profile, now);
                int padId = id;
                var pad = _pads.GetOrCreate(id, (large, small) => _rumble.Enqueue((padId, (ushort)(large * 257), (ushort)(small * 257))));
                pad?.Send(result.Xbox);
                wantedKeys.UnionWith(result.Keys);
                if (mapper.MeasuredTurboHz is double hz && (double.IsNaN(measured) || hz > measured)) measured = hz;
            }
            Volatile.Write(ref _measuredTurboHz, measured);

            _detectedTurbo = detected is { } now1 ? new DetectionBox { Value = now1 } : null;
            // Avisa na primeira detecção, quando o botão muda ou quando a frequência muda de verdade (≥ 1 Hz).
            if (detected is { } current && (lastReported is not { } previous || previous.Button != current.Button || Math.Abs(previous.Hz - current.Hz) >= 1.0))
            {
                lastReported = current;
                ControllerTurboDetected?.Invoke(current);
            }
            else if (detected == null) lastReported = null;

            if (!forwarding && _pads.Count > 0) { _pads.RemoveAll(); mappers.Clear(); }

            // Teclas: aperta as novas e solta as que não são mais pedidas.
            foreach (var vk in wantedKeys) if (keysDown.Add(vk)) KeyboardOutput.Down(vk);
            foreach (var vk in keysDown.Where(k => !wantedKeys.Contains(k)).ToList()) { KeyboardOutput.Up(vk); keysDown.Remove(vk); }

            // Vibração pedida pelo jogo ao controle virtual → controle físico (precisa ser na thread do SDL).
            while (_rumble.TryDequeue(out var r)) reader.Rumble(r.Id, r.Low, r.High);
        }

        static IReadOnlySet<string> PhysicalDown(ControllerSnapshot s)
        {
            var down = new HashSet<string>(s.PressedStandard);
            if (s.LeftTrigger > 0.3f) down.Add("LT");
            if (s.RightTrigger > 0.3f) down.Add("RT");
            return down;
        }

        void ReleaseKeys()
        {
            foreach (var vk in keysDown) KeyboardOutput.Up(vk);
            keysDown.Clear();
        }
    }

    public void Dispose()
    {
        _stop = true;
        if (_thread.IsAlive) _thread.Join(2000);
    }

    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint period);
}
