namespace Virtual8BitDo.App.Services;

/// <summary>
/// Garante uma única cópia do app. A segunda cópia avisa a primeira (por um evento nomeado do Windows)
/// e encerra; a primeira, que pode estar escondida na bandeja, se mostra.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string Name = @"Local\Virtual8BitDo.SingleInstance";
    private const string ShowEventName = @"Local\Virtual8BitDo.ShowWindow";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _showEvent;
    private readonly bool _isFirst;
    private RegisteredWaitHandle? _registration;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, Name, out _isFirst);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
    }

    /// <summary>True se esta é a primeira cópia. Se for false, o pedido para mostrar a janela já foi enviado.</summary>
    public bool TryBecomePrimary()
    {
        if (!_isFirst) _showEvent.Set();
        return _isFirst;
    }

    /// <summary>Chama <paramref name="onShowRequested"/> (em outra thread) quando uma segunda cópia for aberta.</summary>
    public void ListenForShowRequests(Action onShowRequested)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent, (_, _) => onShowRequested(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _showEvent.Dispose();
        if (_isFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
