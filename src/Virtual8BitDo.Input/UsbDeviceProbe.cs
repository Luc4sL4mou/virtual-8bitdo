using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Virtual8BitDo.Input;

/// <summary>
/// Consulta os dispositivos USB do Windows para distinguir cabo de dongle quando os dois usam o mesmo VID/PID.
///
/// Como funciona (ex.: 8BitDo Ultimate 2, PID 0x310B no modo Xbox):
///  - O dongle, ocioso (sem controle ligado a ele), aparece com outro PID (0x6013): o "PID do receptor".
///  - Quando o controle se conecta pelo dongle, o dongle passa a se apresentar como 0x310B, com o número de série DELE.
///  - Pelo cabo, o 0x310B tem o número de série do CONTROLE.
/// Então: se o serial do 0x310B já foi visto como receptor (o Windows guarda esse histórico), é dongle; senão, é cabo.
/// </summary>
[SupportedOSPlatform("windows")]
public static class UsbDeviceProbe
{
    public static (ConnectionType Connection, string Detail) ClassifyReceiverLink(ushort vid, ushort pid, ushort receiverPid)
    {
        var present = PresentSerials(vid, pid);
        if (present.Count != 1)
            return (ConnectionType.Unknown, present.Count == 0 ? "USB: dispositivo não encontrado" : "USB: mais de um dispositivo igual");

        string serial = present[0];
        var receivers = KnownSerials(vid, receiverPid);
        return receivers.Contains(serial, StringComparer.OrdinalIgnoreCase)
            ? (ConnectionType.Dongle24G, $"USB serial {serial} = receptor 2.4G")
            : (ConnectionType.Usb, $"USB serial {serial} = controle no cabo");
    }

    /// <summary>Números de série dos dispositivos USB com esse VID/PID conectados agora.</summary>
    public static IReadOnlyList<string> PresentSerials(ushort vid, ushort pid)
    {
        string prefix = $@"USB\VID_{vid:X4}&PID_{pid:X4}\";
        return PresentUsbDeviceIds()
            .Where(id => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(id => id[prefix.Length..])
            .ToList();
    }

    /// <summary>Números de série que já apareceram alguma vez com esse VID/PID (histórico do Windows).</summary>
    public static IReadOnlyList<string> KnownSerials(ushort vid, ushort pid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\USB\VID_{vid:X4}&PID_{pid:X4}");
            return key?.GetSubKeyNames() ?? Array.Empty<string>();
        }
        catch (Exception) { return Array.Empty<string>(); }
    }

    // ---- Lista de dispositivos presentes (Configuration Manager do Windows) ----
    private const int CM_GETIDLIST_FILTER_ENUMERATOR = 0x1, CM_GETIDLIST_FILTER_PRESENT = 0x100;
    private const int Flags = CM_GETIDLIST_FILTER_ENUMERATOR | CM_GETIDLIST_FILTER_PRESENT;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_List_SizeW(out int length, string filter, int flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_ID_ListW(string filter, char[] buffer, int length, int flags);

    private static IEnumerable<string> PresentUsbDeviceIds()
    {
        if (CM_Get_Device_ID_List_SizeW(out int len, "USB", Flags) != 0 || len <= 0) return Array.Empty<string>();
        var buffer = new char[len];
        if (CM_Get_Device_ID_ListW("USB", buffer, len, Flags) != 0) return Array.Empty<string>();
        return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
