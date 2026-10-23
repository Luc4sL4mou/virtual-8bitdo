using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace Virtual8BitDo.Output;

public enum VirtualBusStatus
{
    /// <summary>ViGEmBus conectado: dá para criar controles virtuais.</summary>
    Ready,
    /// <summary>O driver ViGEmBus não está instalado.</summary>
    DriverMissing,
    /// <summary>Outro erro ao falar com o driver (ver <see cref="VirtualPadManager.Error"/>).</summary>
    Error,
}

/// <summary>
/// Cria e remove controles virtuais, um para cada controle físico repassado.
/// Use sempre da mesma thread (o motor de entrada).
/// </summary>
public sealed class VirtualPadManager : IDisposable
{
    private readonly ViGEmClient? _client;
    private readonly Dictionary<int, VirtualXboxPad> _pads = new();
    private volatile int _count;

    public VirtualBusStatus Status { get; }
    public string? Error { get; private set; }

    /// <summary>Quantos controles virtuais existem agora (pode ser lido de qualquer thread).</summary>
    public int Count => _count;

    public VirtualPadManager()
    {
        try
        {
            _client = new ViGEmClient();
            Status = VirtualBusStatus.Ready;
        }
        catch (VigemBusNotFoundException)
        {
            Status = VirtualBusStatus.DriverMissing;
        }
        catch (Exception ex)
        {
            Status = VirtualBusStatus.Error;
            Error = ex.Message;
        }
    }

    /// <summary>Controle virtual do controle físico <paramref name="id"/>, criado na primeira vez. Null se o driver não está disponível.</summary>
    public VirtualXboxPad? GetOrCreate(int id, Action<byte, byte> onRumble)
    {
        if (_pads.TryGetValue(id, out var pad)) return pad;
        if (_client == null) return null;
        try
        {
            pad = new VirtualXboxPad(_client);
            pad.Rumble += onRumble;
            _pads[id] = pad;
            _count = _pads.Count;
            return pad;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return null;
        }
    }

    public void Remove(int id)
    {
        if (!_pads.Remove(id, out var pad)) return;
        pad.Dispose();
        _count = _pads.Count;
    }

    public void RemoveAll()
    {
        foreach (var id in _pads.Keys.ToList()) Remove(id);
    }

    public void Dispose()
    {
        RemoveAll();
        _client?.Dispose();
    }
}
