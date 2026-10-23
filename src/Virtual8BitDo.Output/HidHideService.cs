using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Virtual8BitDo.Output;

public enum HidHideStatus
{
    NotInstalled,
    /// <summary>Instalado, mas o controle físico está visível para os jogos.</summary>
    Off,
    /// <summary>O controle físico está escondido dos jogos (só o Virtual 8BitDo o enxerga).</summary>
    Hiding,
    Error,
}

/// <summary>
/// Usa o HidHide (pela ferramenta de linha de comando dele) para esconder o controle físico dos jogos,
/// para que eles vejam só o controle virtual e não recebam cada botão duplicado.
///
/// Passos ao esconder: 1) libera o próprio Virtual 8BitDo (senão o app também perderia o controle);
/// 2) põe os dispositivos do controle na lista de escondidos; 3) liga o "esconder" (cloak).
/// Ao mostrar de novo, só desliga o "esconder" (as listas ficam salvas, sem efeito).
/// As chamadas demoram ~100 ms cada: use fora da thread da tela e fora do motor.
/// </summary>
public sealed class HidHideService
{
    private static readonly string[] CandidatePaths =
    {
        @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe",
        @"C:\Program Files\Nefarius Software Solutions e.U\HidHide\x64\HidHideCLI.exe",
    };

    private readonly string? _cli = CandidatePaths.FirstOrDefault(File.Exists);

    public bool IsInstalled => _cli != null;
    public HidHideStatus Status { get; private set; }
    public int HiddenDeviceCount { get; private set; }
    public string? LastError { get; private set; }

    public HidHideService() => Status = IsInstalled ? HidHideStatus.Off : HidHideStatus.NotInstalled;

    /// <summary>Esconde dos jogos os dispositivos de jogo cujo caminho satisfaz <paramref name="isTarget"/>.</summary>
    public HidHideStatus Hide(Func<string, bool> isTarget)
    {
        if (_cli == null) return Status;
        try
        {
            // 1) Libera o próprio app.
            string self = Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do app desconhecido.");
            if (!Run("--app-list").Contains(self, StringComparison.OrdinalIgnoreCase))
                Run("--app-reg", self);

            // 2) Esconde os dispositivos de jogo do controle (cada modo de conexão tem os seus caminhos).
            //    --dev-gaming só lista os caminhos HID (DirectInput). No modo Xbox, a API XInput usa outro caminho
            //    (o dispositivo "XnaComposite"), que também precisa ser escondido, senão o jogo vê o controle duas vezes.
            var alreadyHidden = Run("--dev-list");
            var targets = GamingDevicePaths(Run("--dev-gaming"))
                .Concat(XInputDevicePaths())
                .Where(isTarget)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var path in targets)
                if (!alreadyHidden.Contains(path, StringComparison.OrdinalIgnoreCase))
                    Run("--dev-hide", path);

            // 3) Liga o esconder.
            Run("--cloak-on");
            HiddenDeviceCount = targets.Count;
            LastError = null;
            return Status = HidHideStatus.Hiding;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Status = HidHideStatus.Error;
        }
    }

    /// <summary>Mostra o controle físico de novo para todos os programas.</summary>
    public HidHideStatus Unhide()
    {
        if (_cli == null) return Status;
        try
        {
            Run("--cloak-off");
            HiddenDeviceCount = 0;
            return Status = HidHideStatus.Off;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return Status = HidHideStatus.Error;
        }
    }

    /// <summary>Lê todos os "deviceInstancePath" do JSON de --dev-gaming (o formato tem grupos aninhados).</summary>
    private static IEnumerable<string> GamingDevicePaths(string json)
    {
        var found = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement);
        }
        catch (JsonException) { /* saída inesperada: nada a esconder */ }
        return found.Distinct(StringComparer.OrdinalIgnoreCase);

        void Walk(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var p in e.EnumerateObject())
                    {
                        if (p.NameEquals("deviceInstancePath") && p.Value.ValueKind == JsonValueKind.String)
                            found.Add(p.Value.GetString()!);
                        else
                            Walk(p.Value);
                    }
                    break;
                case JsonValueKind.Array:
                    foreach (var item in e.EnumerateArray()) Walk(item);
                    break;
            }
        }
    }

    // ---- Dispositivos XInput (classe XnaComposite / XboxComposite) presentes ----
    private const int CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100;
    private const int CM_DRP_CLASS = 0x08;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out int length, string filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, int length, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out int devInst, string deviceId, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_DevNode_Registry_PropertyW(int devInst, int property, out int type, char[] buffer, ref int length, int flags);

    private static IEnumerable<string> XInputDevicePaths()
    {
        const int flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;
        if (CM_Get_Device_ID_List_SizeW(out int len, "USB", flags) != 0 || len <= 0) yield break;
        var list = new char[len];
        if (CM_Get_Device_ID_ListW("USB", list, len, flags) != 0) yield break;

        foreach (var id in new string(list).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (CM_Locate_DevNodeW(out int devInst, id, 0) != 0) continue;
            var buffer = new char[256];
            int size = buffer.Length * 2;
            if (CM_Get_DevNode_Registry_PropertyW(devInst, CM_DRP_CLASS, out _, buffer, ref size, 0) != 0) continue;
            var cls = new string(buffer).TrimEnd('\0');
            if (cls is "XnaComposite" or "XboxComposite") yield return id;
        }
    }

    private string Run(params string[] args)
    {
        var psi = new ProcessStartInfo(_cli!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o HidHideCLI.");
        string output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(5000)) { p.Kill(); throw new TimeoutException("HidHideCLI não respondeu."); }
        if (p.ExitCode != 0) throw new InvalidOperationException($"HidHideCLI {string.Join(' ', args)} falhou: {p.StandardError.ReadToEnd().Trim()}");
        return output;
    }
}
