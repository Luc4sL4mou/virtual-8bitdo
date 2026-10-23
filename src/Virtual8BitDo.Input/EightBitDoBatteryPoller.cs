using HidSharp;

namespace Virtual8BitDo.Input;

/// <summary>
/// Lê a bateria dos controles 8BitDo no modo Xbox (dongle 2.4G ou cabo), onde nem o SDL nem o XInput informam o nível.
///
/// Usa o canal de configuração da 8BitDo (uma interface HID do fabricante, 64 bytes, ao lado da de Xbox).
/// Envia UMA consulta de leitura documentada pelo projeto 8bitdo-ultimate2-linux (docs/PROTOCOL.md):
///   pedido:   81 04 21 01 00 00 06  (resto zerado)
///   resposta: 02 04 04 00 21 01 ...  com a bateria em % no byte 18.
/// Essa consulta só lê; nenhum comando de gravação (configuração/flash) é usado aqui.
/// Roda em segundo plano, a cada 30 s, para não travar a interface.
/// </summary>
internal sealed class EightBitDoBatteryPoller : IDisposable
{
    public const ushort Vendor8BitDo = 0x2DC8;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly byte[] Query = { 0x81, 0x04, 0x21, 0x01, 0x00, 0x00, 0x06 };

    private readonly HidDevice _device;
    private readonly CancellationTokenSource _cts = new();
    private volatile int _percent = -1;

    private EightBitDoBatteryPoller(HidDevice device)
    {
        _device = device;
        _ = Task.Run(LoopAsync);
    }

    /// <summary>Último nível lido (0..100), ou null se ainda não houve resposta.</summary>
    public int? Percent => _percent >= 0 ? _percent : null;

    /// <summary>Cria o leitor se o controle tiver o canal de configuração da 8BitDo; senão, null.</summary>
    public static EightBitDoBatteryPoller? TryCreate(ushort vid, ushort pid)
    {
        if (vid != Vendor8BitDo) return null;
        try
        {
            var channel = DeviceList.Local.GetHidDevices(vid, pid)
                .FirstOrDefault(d => d.GetMaxOutputReportLength() == 64 && d.GetMaxInputReportLength() == 64);
            return channel == null ? null : new EightBitDoBatteryPoller(channel);
        }
        catch (Exception) { return null; }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (ReadOnce() is int p) _percent = p;
            try { await Task.Delay(Interval, _cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private int? ReadOnce()
    {
        try
        {
            if (!_device.TryOpen(out var stream)) return null;
            using (stream)
            {
                stream.ReadTimeout = 300;
                var request = new byte[64];
                Query.CopyTo(request, 0);
                stream.Write(request);

                // A resposta pode vir depois de outros relatórios; procura a que ecoa o comando 0x0121.
                var until = DateTime.UtcNow.AddSeconds(1);
                while (DateTime.UtcNow < until)
                {
                    byte[] r;
                    try { r = stream.Read(); }
                    catch (TimeoutException) { continue; }
                    if (r.Length > 18 && r[0] == 0x02 && r[1] == 0x04 && r[4] == 0x21 && r[5] == 0x01)
                        return r[18] <= 100 ? r[18] : null;
                }
            }
        }
        catch (Exception) { /* controle desconectou no meio da leitura, etc.: tenta de novo no próximo ciclo */ }
        return null;
    }

    public void Dispose() => _cts.Cancel();
}
